using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostHog;
using PostHog.Config;
using PostHog.Features;
using PostHog.Versioning;
using UnitTests.Fakes;

namespace FeatureFlagTests;

public class TheIsFeatureFlagEnabledAsyncMethod
{
    [Theory] // Ported from PostHog/posthog-python test_feature_enabled_simple
    // and test_feature_enabled_simple_is_false
    // and test_feature_enabled_simple_is_true_when_rollout_is_undefined
    [InlineData(true, "100", true)]
    [InlineData(true, "null", true)]
    [InlineData(false, "100", false)]
    [InlineData(true, "0", false)]
    public async Task ReturnsTrueWhenFeatureFlagIsEnabled(bool active, string rolloutPercentage, bool expected)
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
#pragma warning disable CA1308
            $$"""
              {
                 "flags":[
                    {
                       "id":1,
                       "name":"Beta Feature",
                       "key":"beta-feature",
                       "is_simple_flag":true,
                       "active":{{active.ToString().ToLowerInvariant()}},
                       "rollout_percentage":100,
                       "filters":{
                          "groups":[
                             {
                                "properties":[
                                   
                                ],
                                "rollout_percentage":{{rolloutPercentage}}
                             }
                          ]
                       }
                    }
                 ]
              }
              """
#pragma warning restore CA1308
        );
        var client = container.Activate<PostHogClient>();

        Assert.Equal(expected, await client.IsFeatureEnabledAsync("beta-feature", "distinct-id"));
    }

    [Fact] // Ported from PostHog/posthog-python test_feature_enabled_doesnt_exist
    public async Task ReturnsNullWhenFlagDoesNotExist()
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddDecideResponse("""{"featureFlags": {}}""");
        var client = container.Activate<PostHogClient>();
        Assert.False(await client.IsFeatureEnabledAsync("doesnt-exist", "distinct-id"));
        container.FakeHttpMessageHandler.AddDecideResponseException(new HttpRequestException());
        Assert.Null(await client.IsFeatureEnabledAsync("doesnt-exist", "distinct-id"));
    }

    [Fact] // Ported from PostHog/posthog-python test_personal_api_key_doesnt_exist
    public async Task ReturnsDecideResultWhenNoPersonalApiKey()
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddDecideResponse("""{"featureFlags": {"feature-flag": true}}""");
        var client = container.Activate<PostHogClient>();
        Assert.True(await client.IsFeatureEnabledAsync("feature-flag", "distinct-id"));
    }

    [Fact]
    public async Task CapturesFeatureFlagCalledEventOnlyOncePerDistinctIdFlagKeyAndResponse()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddLocalEvaluationResponse(
            """
            { 
              "flags": [
                {
                    "key": "flag-key",
                    "active": true,
                    "rollout_percentage": 100,
                    "filters": {
                        "groups": [
                            {
                                "properties": [],
                                "rollout_percentage": 100
                            }
                        ]
                    }
                }
              ]
            } 
            """
        );
        var captureRequestHandler = messageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        Assert.True(await client.IsFeatureEnabledAsync("flag-key", "a-distinct-id"));
        await client.IsFeatureEnabledAsync("flag-key", "a-distinct-id"); // Cache hit, not captured.
        Assert.True(await client.IsFeatureEnabledAsync("flag-key", "another-distinct-id"));
        await client.IsFeatureEnabledAsync("flag-key", "another-distinct-id"); // Cache hit

        client.ClearLocalFlagsCache();
        messageHandler.AddLocalEvaluationResponse(
            """
            { 
              "flags": [
                {
                    "key": "flag-key",
                    "active": true,
                    "rollout_percentage": 0,
                    "filters": {
                        "groups": [
                            {
                                "properties": [],
                                "rollout_percentage": 0
                            }
                        ]
                    }
                }
              ]
            } 
            """
        );
        Assert.False(
            await client.IsFeatureEnabledAsync("flag-key", "another-distinct-id")); // Not a cache-hit, new response

        await client.FlushAsync();
        var received = captureRequestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal(
            $$"""
              {
                "api_key": "fake-project-api-key",
                "historical_migrations": false,
                "batch": [
                  {
                    "event": "$feature_flag_called",
                    "properties": {
                      "$feature_flag": "flag-key",
                      "$feature_flag_response": true,
                      "locally_evaluated": false,
                      "$feature/flag-key": true,
                      "distinct_id": "a-distinct-id",
                      "$lib": "posthog-dotnet",
                      "$lib_version": "{{client.Version}}",
                      "$geoip_disable": true,
                      "$active_feature_flags": [
                        "flag-key"
                      ]
                    },
                    "timestamp": "2024-01-21T19:08:23\u002B00:00"
                  },
                  {
                    "event": "$feature_flag_called",
                    "properties": {
                      "$feature_flag": "flag-key",
                      "$feature_flag_response": true,
                      "locally_evaluated": false,
                      "$feature/flag-key": true,
                      "distinct_id": "another-distinct-id",
                      "$lib": "posthog-dotnet",
                      "$lib_version": "{{client.Version}}",
                      "$geoip_disable": true,
                      "$active_feature_flags": [
                        "flag-key"
                      ]
                    },
                    "timestamp": "2024-01-21T19:08:23\u002B00:00"
                  },
                  {
                    "event": "$feature_flag_called",
                    "properties": {
                      "$feature_flag": "flag-key",
                      "$feature_flag_response": false,
                      "locally_evaluated": false,
                      "$feature/flag-key": false,
                      "distinct_id": "another-distinct-id",
                      "$lib": "posthog-dotnet",
                      "$lib_version": "{{client.Version}}",
                      "$geoip_disable": true,
                      "$active_feature_flags": []
                    },
                    "timestamp": "2024-01-21T19:08:23\u002B00:00"
                  }
                ]
              }
              """
            , received);
    }

    [Fact]
    public async Task CapturesFeatureFlagCalledEventOnlyOncePerDistinctIdFlagKeyAndResponseWithDecide()
    {
        var container = new TestContainer();
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddRepeatedDecideResponse(4,
            """
            {"featureFlags": {"flag-key": true}}
            """
        );
        var captureRequestHandler = messageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        Assert.True(await client.IsFeatureEnabledAsync("flag-key", "a-distinct-id"));
        await client.IsFeatureEnabledAsync("flag-key", "a-distinct-id"); // Cache hit, not captured.
        Assert.True(await client.IsFeatureEnabledAsync("flag-key", "another-distinct-id"));
        await client.IsFeatureEnabledAsync("flag-key", "another-distinct-id"); // Cache hit

        client.ClearLocalFlagsCache();
        messageHandler.AddDecideResponse(
            """
            { 
              "flags": [
                {
                    "key": "flag-key",
                    "active": true,
                    "rollout_percentage": 0,
                    "filters": {
                        "groups": [
                            {
                                "properties": [],
                                "rollout_percentage": 0
                            }
                        ]
                    }
                }
              ]
            } 
            """
        );
        Assert.False(
            await client.IsFeatureEnabledAsync("flag-key", "another-distinct-id")); // Not a cache-hit, new response

        await client.FlushAsync();
        var received = captureRequestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal(
            $$"""
              {
                "api_key": "fake-project-api-key",
                "historical_migrations": false,
                "batch": [
                  {
                    "event": "$feature_flag_called",
                    "properties": {
                      "$feature_flag": "flag-key",
                      "$feature_flag_response": true,
                      "locally_evaluated": false,
                      "$feature/flag-key": true,
                      "distinct_id": "a-distinct-id",
                      "$lib": "posthog-dotnet",
                      "$lib_version": "{{client.Version}}",
                      "$geoip_disable": true
                    },
                    "timestamp": "2024-01-21T19:08:23\u002B00:00"
                  },
                  {
                    "event": "$feature_flag_called",
                    "properties": {
                      "$feature_flag": "flag-key",
                      "$feature_flag_response": true,
                      "locally_evaluated": false,
                      "$feature/flag-key": true,
                      "distinct_id": "another-distinct-id",
                      "$lib": "posthog-dotnet",
                      "$lib_version": "{{client.Version}}",
                      "$geoip_disable": true
                    },
                    "timestamp": "2024-01-21T19:08:23\u002B00:00"
                  },
                  {
                    "event": "$feature_flag_called",
                    "properties": {
                      "$feature_flag": "flag-key",
                      "$feature_flag_response": false,
                      "locally_evaluated": false,
                      "$feature/flag-key": false,
                      "distinct_id": "another-distinct-id",
                      "$lib": "posthog-dotnet",
                      "$lib_version": "{{client.Version}}",
                      "$geoip_disable": true
                    },
                    "timestamp": "2024-01-21T19:08:23\u002B00:00"
                  }
                ]
              }
              """
            , received);
    }

    [Fact]
    public async Task DoesNotCaptureFeatureFlagCalledEventWhenSendFeatureFlagsFalse()
    {
        var container = new TestContainer();
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddRepeatedDecideResponse(
            count: 4,
            count => $$"""{"featureFlags": {"flag-key": "feature-value-{{count}}"} }""");
        var captureRequestHandler = messageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        await client.IsFeatureEnabledAsync(featureKey: "flag-key",
            distinctId: "a-distinct-id", options: new FeatureFlagOptions { SendFeatureFlagEvents = false });

        await client.FlushAsync();
        Assert.Empty(captureRequestHandler.ReceivedRequests);
    }

    [Fact]
    public async Task CapturesFeatureFlagRealExample()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
                "flags": [
                    {
                        "id": 91866,
                        "team_id": 110510,
                        "name": "A multivariate feature flag that tells you what character you are",
                        "key": "hogtied_got_character",
                        "filters": {
                            "groups": [
                                {
                                    "variant": "cersei",
                                    "properties": [
                                        {
                                            "key": "join_date",
                                            "type": "person",
                                            "value": "-14d",
                                            "operator": "is_date_before"
                                        },
                                        {
                                            "key": "leave_date",
                                            "type": "person",
                                            "value": "2025-01-24 14:20:00",
                                            "operator": "is_date_after"
                                        }
                                    ],
                                    "rollout_percentage": 100
                                },
                                {
                                    "variant": "Cersei",
                                    "properties": [
                                        {
                                            "key": "email",
                                            "type": "person",
                                            "value": [
                                                "haacked@gmail.com"
                                            ],
                                            "operator": "exact"
                                        }
                                    ],
                                    "rollout_percentage": 100
                                }
                            ],
                            "payloads": {
                                "Cersei": "25",
                                "cersei": "{\"role\": \"burn it all down\"}",
                                "tyrion": "100",
                                "danaerys": "{\"role\": \"khaleesi\"}",
                                "jon-snow": "{\"role\": \"king of the north\"}"
                            },
                            "multivariate": {
                                "variants": [
                                    {
                                        "key": "tyrion",
                                        "name": "The one who talks",
                                        "rollout_percentage": 25
                                    },
                                    {
                                        "key": "danaerys",
                                        "name": "The mother of dragons",
                                        "rollout_percentage": 25
                                    },
                                    {
                                        "key": "jon-snow",
                                        "name": "Knows nothing",
                                        "rollout_percentage": 25
                                    },
                                    {
                                        "key": "cersei",
                                        "name": "Not nice",
                                        "rollout_percentage": 15
                                    },
                                    {
                                        "key": "Cersei",
                                        "name": "Capital",
                                        "rollout_percentage": 10
                                    }
                                ]
                            }
                        },
                        "deleted": false,
                        "active": true,
                        "ensure_experience_continuity": false
                    }
                ],
                "group_type_mapping": {
                    "0": "organization",
                    "1": "project"
                },
                "cohorts": {}
            }
            """
        );
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.IsFeatureEnabledAsync(
            "hogtied_got_character",
            distinctId: "659df793-429a-4517-84ff-747dfc103e6c",
            options: new FeatureFlagOptions
            {
                PersonProperties = new Dictionary<string, object?>
                {
                    ["join_date"] = "2023-02-02",
                    ["leave_date"] = "2025-02-02"
                },
                Groups =
                [
                    new Group("organization", "01943db3-83be-0000-e7ea-ecae4d9b5afb"),
                    new Group("project", "aaaa-bbbb-cccc", new Dictionary<string, object?>
                    {
                        ["size"] = "large"
                    })
                ],
                OnlyEvaluateLocally = true
            });

        Assert.True(result);

        await client.FlushAsync();
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        // NOTE: $active_feature_flags is empty because the person properties is not passed
        // through to the capture call when evaluating a feature flag. Should we pass it through?
        // TODO: I need to confer with the authors of the other libraries.
        Assert.Equal(
            $$"""
              {
                "api_key": "fake-project-api-key",
                "historical_migrations": false,
                "batch": [
                  {
                    "event": "$feature_flag_called",
                    "properties": {
                      "$feature_flag": "hogtied_got_character",
                      "$feature_flag_response": "cersei",
                      "locally_evaluated": false,
                      "$feature/hogtied_got_character": "cersei",
                      "distinct_id": "659df793-429a-4517-84ff-747dfc103e6c",
                      "$lib": "posthog-dotnet",
                      "$lib_version": "{{VersionConstants.Version}}",
                      "$geoip_disable": true,
                      "$groups": {
                        "organization": "01943db3-83be-0000-e7ea-ecae4d9b5afb",
                        "project": "aaaa-bbbb-cccc"
                      },
                      "$active_feature_flags": []
                    },
                    "timestamp": "2024-01-21T19:08:23\u002B00:00"
                  }
                ]
              }
              """, received);
    }
}

public class TheGetFeatureFlagAsyncMethod
{
    [Fact] // Ported from PostHog/posthog-python test_flag_person_properties
    public async Task MatchesOnPersonProperties()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":1,
                     "name":"Beta Feature",
                     "key":"person-flag",
                     "is_simple_flag":true,
                     "active":true,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"region",
                                    "operator":"exact",
                                    "value":[
                                       "USA"
                                    ],
                                    "type":"person"
                                 }
                              ],
                              "rollout_percentage":100
                           }
                        ]
                     }
                  }
               ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        Assert.True(
            await client.GetFeatureFlagAsync(
                "person-flag",
                distinctId: "some-distinct-id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["region"] = "USA" }
                })
        );
        Assert.False(
            await client.GetFeatureFlagAsync(
                "person-flag",
                distinctId: "some-distinct-2",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["region"] = "Canada" }
                })
        );
    }

    [Fact] // Ported from PostHog/posthog-python test_flag_person_properties
    public async Task MatchesOnPersonPropertiesCaseSensitively()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":1,
                     "name":"Beta Feature",
                     "key":"person-flag",
                     "active":true,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"region",
                                    "operator":"exact",
                                    "value":[
                                       "USA"
                                    ],
                                    "type":"person"
                                 }
                              ],
                              "rollout_percentage":100
                           }
                        ]
                     }
                  },
                  {
                    "id":2,
                    "name":"Beta Feature",
                    "key":"PERSON-FLAG",
                    "active":true,
                    "filters":{
                       "groups":[
                          {
                             "properties":[
                                {
                                   "key":"region",
                                   "operator":"exact",
                                   "value":[
                                      "Canada"
                                   ],
                                   "type":"person"
                                }
                             ],
                             "rollout_percentage":100
                          }
                       ]
                    }
                 }
               ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        Assert.True(
            await client.GetFeatureFlagAsync(
                "person-flag",
                distinctId: "some-distinct-id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["region"] = "USA" }
                })
        );
        Assert.False(
            await client.GetFeatureFlagAsync(
                "PERSON-FLAG",
                distinctId: "some-distinct-id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["region"] = "USA" }
                })
        );
        Assert.False(
            await client.GetFeatureFlagAsync(
                "person-flag",
                distinctId: "some-distinct-id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["region"] = "Canada" }
                })
        );
        Assert.True(
            await client.GetFeatureFlagAsync(
                "PERSON-FLAG",
                distinctId: "some-distinct-id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["region"] = "Canada" }
                })
        );
    }

    [Fact] // Ported from PostHog/posthog-python test_flag_group_properties
    public async Task MatchesOnGroupProperties()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
              "flags":[
                 {
                    "id":1,
                    "name":"Beta Feature",
                    "key":"group-flag",
                    "is_simple_flag":true,
                    "active":true,
                    "filters":{
                       "aggregation_group_type_index":0,
                       "groups":[
                          {
                             "properties":[
                                {
                                   "group_type_index":0,
                                   "key":"name",
                                   "operator":"exact",
                                   "value":[
                                      "Project Name 1"
                                   ],
                                   "type":"group"
                                }
                             ],
                             "rollout_percentage":35
                          }
                       ]
                    }
                 }
              ],
              "group_type_mapping": {"0": "company", "1": "project"}
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        var noMatchBecauseNoGroupNames = await client.GetFeatureFlagAsync(
            featureKey: "group-flag",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                Groups =
                [
                    new Group(
                        GroupType: "company",
                        GroupKey: "company",
                        Properties: new Dictionary<string, object?>
                        {
                            ["name"] = "Project Name 1"
                        })
                ]
            }
        );
        var noMatchBecauseCompanyNameDoesNotMatchFilter = await client.GetFeatureFlagAsync(
            featureKey: "group-flag",
            distinctId: "some-distinct-2",
            options: new FeatureFlagOptions
            {
                Groups =
                [
                    new Group(
                        GroupType: "company",
                        GroupKey: "company",
                        Properties: new Dictionary<string, object?>
                        {
                            ["name"] = "Project Name 2"
                        })
                ]
            });
        var match = await client.GetFeatureFlagAsync(
            featureKey: "group-flag",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                Groups =
                [
                    new Group(
                        GroupType: "company",
                        GroupKey: "amazon_without_rollout",
                        Properties: new Dictionary<string, object?>
                        {
                            ["name"] = "Project Name 1"
                        })
                ]
            });
        var notMatchBecauseRollout = await client.GetFeatureFlagAsync(
            featureKey: "group-flag",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                Groups =
                [
                    new Group(
                        GroupType: "company",
                        GroupKey: "amazon",
                        Properties: new Dictionary<string, object?>
                        {
                            ["name"] = "Project Name 1"
                        })
                ]
            });
        var propertyMismatch = await client.GetFeatureFlagAsync(
            featureKey: "group-flag",
            distinctId: "some-distinct-2",
            options: new FeatureFlagOptions
            {
                Groups =
                [
                    new Group(
                        GroupType: "company",
                        GroupKey: "amazon_without_rollout",
                        Properties: new Dictionary<string, object?>
                        {
                            ["name"] = "Project Name 2"
                        })
                ]
            }
        );

        // Going to fallback to decide
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
            {"featureFlags": {"group-flag": "decide-fallback-value"}}
            """
        );
        client.ClearLocalFlagsCache();
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
              "flags":[
                 {
                    "id":1,
                    "name":"Beta Feature",
                    "key":"group-flag",
                    "is_simple_flag":true,
                    "active":true,
                    "filters":{
                       "aggregation_group_type_index":0,
                       "groups":[
                          {
                             "properties":[
                                {
                                   "group_type_index":0,
                                   "key":"name",
                                   "operator":"exact",
                                   "value":[
                                      "Project Name 1"
                                   ],
                                   "type":"group"
                                }
                             ],
                             "rollout_percentage":35
                          }
                       ]
                    }
                 }
              ],
              "group_type_mapping": {}
            }
            """
        );
        var decideResult = await client.GetFeatureFlagAsync(
            featureKey: "group-flag",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                Groups =
                [
                    new Group(
                        GroupType: "company",
                        GroupKey: "amazon",
                        Properties: new Dictionary<string, object?>
                        {
                            ["name"] = "Project Name 1"
                        })
                ]
            }
        );
        Assert.False(noMatchBecauseNoGroupNames);
        Assert.False(noMatchBecauseCompanyNameDoesNotMatchFilter);
        Assert.True(match);
        Assert.False(propertyMismatch);
        Assert.False(notMatchBecauseRollout);
        Assert.Equal("decide-fallback-value", decideResult);
    }

    [Fact] // Ported from PostHog/posthog-python test_flag_with_complex_definition
    public async Task ReturnsCorrectValueForComplexFlags()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
             "flags": [
                   {
                       "id": 1,
                       "name": "Beta Feature",
                       "key": "complex-flag",
                       "is_simple_flag": false,
                       "active": true,
                       "filters": {
                           "groups": [
                               {
                                   "properties": [
                                       {
                                           "key": "region",
                                           "operator": "exact",
                                           "value": ["USA"],
                                           "type": "person"
                                       },
                                       {
                                           "key": "name",
                                           "operator": "exact",
                                           "value": ["Aloha"],
                                           "type": "person"
                                       }
                                   ],
                                   "rollout_percentage": 100
                               },
                               {
                                   "properties": [
                                       {
                                           "key": "email",
                                           "operator": "exact",
                                           "value": ["a@b.com", "b@c.com"],
                                           "type": "person"
                                       }
                                   ],
                                   "rollout_percentage": 30
                               },
                               {
                                   "properties": [
                                       {
                                           "key": "doesnt_matter",
                                           "operator": "exact",
                                           "value": ["1", "2"],
                                           "type": "person"
                                       }
                                   ],
                                   "rollout_percentage": 0
                               }
                           ]
                       }
                   }
               ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        Assert.True(
            await client.GetFeatureFlagAsync(
                featureKey: "complex-flag",
                distinctId: "some-distinct-id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["region"] = "USA", ["name"] = "Aloha" }
                })
        );

        // this distinctIDs hash is < rollout %
        Assert.True(
            await client.GetFeatureFlagAsync(
                featureKey: "complex-flag",
                distinctId: "some-distinct-id_within_rollout?",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["region"] = "USA", ["email"] = "a@b.com" }
                })
        );

        // will fall back on `/decide`, as all properties present for second group, but that group resolves to false
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
            {"featureFlags": {"complex-flag": "decide-fallback-value"}}
            """
        );
        Assert.Equal(
            "decide-fallback-value",
            await client.GetFeatureFlagAsync(
                featureKey: "complex-flag",
                distinctId: "some-distinct-id_outside_rollout?",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new()
                    {
                        ["region"] = "USA",
                        ["email"] = "a@b.com"
                    }
                })
        );
        // Same as above
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
            {"featureFlags": {"complex-flag": "decide-fallback-value"}}
            """
        );
        Assert.Equal(
            "decide-fallback-value",
            await client.GetFeatureFlagAsync(
                featureKey: "complex-flag",
                distinctId: "some-distinct-id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["doesnt_matter"] = "1" }
                })
        );

        // this one will need to fall back
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
            {"featureFlags": {"complex-flag": "decide-fallback-value"}}
            """
        );
        Assert.Equal(
            "decide-fallback-value",
            await client.GetFeatureFlagAsync(
                featureKey: "complex-flag",
                distinctId: "some-distinct-id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["region"] = "USA" }
                })
        );

        // Won't need to fallback
        Assert.False(
            await client.GetFeatureFlagAsync(
                featureKey: "complex-flag",
                distinctId: "some-distinct-id_outside_rollout?",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new()
                    {
                        ["region"] = "USA",
                        ["email"] = "a@b.com",
                        ["name"] = "x",
                        ["doesnt_matter"] = "1"
                    }
                })
        );
    }

    [Fact] // Ported from PostHog/posthog-python test_feature_flags_fallback_to_decide
    public async Task CanFallbackToDecide()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":1,
                     "name":"Beta Feature",
                     "key":"beta-feature",
                     "is_simple_flag":true,
                     "active":true,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"id",
                                    "value":98,
                                    "operator": null,
                                    "type":"cohort"
                                 }
                              ],
                              "rollout_percentage":100
                           }
                        ]
                     }
                  },
                  {
                     "id":2,
                     "name":"Beta Feature",
                     "key":"beta-feature2",
                     "is_simple_flag":false,
                     "active":true,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"region",
                                    "operator":"exact",
                                    "value":[
                                       "USA"
                                    ],
                                    "type":"person"
                                 }
                              ],
                              "rollout_percentage":100
                           }
                        ]
                     }
                  }
               ]
            }
            """
        );
        container.FakeHttpMessageHandler.AddRepeatedDecideResponse(
            count: 2,
            """
            {"featureFlags": {"beta-feature": "alakazam", "beta-feature2": "alakazam2"}}
            """
        );
        var client = container.Activate<PostHogClient>();

        // beta-feature fallbacks to decide because property type is unknown
        Assert.Equal("alakazam", await client.GetFeatureFlagAsync("beta-feature", "some-distinct-id"));

        // beta-feature2 fallbacks to decide because region property not given with call
        Assert.Equal("alakazam2", await client.GetFeatureFlagAsync("beta-feature2", "some-distinct-id"));
    }

    [Fact] // Ported from PostHog/posthog-python test_feature_flags_dont_fallback_to_decide_when_only_local_evaluation_is_true
    public async Task DoesNotFallbackToDecideWhenOnlyEvaluateLocallyIsTrue()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddRepeatedDecideResponse(
            count: 2,
            """
            {"featureFlags": {"beta-feature": "alakazam", "beta-feature2": "alakazam2"}}
            """
        );
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":1,
                     "name":"Beta Feature",
                     "key":"beta-feature",
                     "is_simple_flag":true,
                     "active":true,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"id",
                                    "value":98,
                                    "operator": null,
                                    "type":"cohort"
                                 }
                              ],
                              "rollout_percentage":100
                           }
                        ]
                     }
                  },
                  {
                     "id":2,
                     "name":"Beta Feature",
                     "key":"beta-feature2",
                     "is_simple_flag":false,
                     "active":true,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"region",
                                    "operator":"exact",
                                    "value":[
                                       "USA"
                                    ],
                                    "type":"person"
                                 }
                              ],
                              "rollout_percentage":100
                           }
                        ]
                     }
                  }
               ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        // beta-feature should fallback to decide because property type is unknown,
        // but doesn't because only_evaluate_locally is true
        Assert.Null(
            await client.GetFeatureFlagAsync(
                featureKey: "beta-feature",
                distinctId: "some-distinct-id",
                options: new FeatureFlagOptions { OnlyEvaluateLocally = true })
        );
        Assert.Null(
            await client.IsFeatureEnabledAsync(
                featureKey: "beta-feature",
                distinctId: "some-distinct-id",
                options: new FeatureFlagOptions { OnlyEvaluateLocally = true })
        );

        // beta-feature2 should fallback to decide because region property not given with call
        // but doesn't because only_evaluate_locally is true
        Assert.Null(
            await client.GetFeatureFlagAsync(
                featureKey: "beta-feature2",
                distinctId: "some-distinct-id",
                options: new FeatureFlagOptions { OnlyEvaluateLocally = true })
        );
        Assert.Null(
            await client.IsFeatureEnabledAsync(
                featureKey: "beta-feature2",
                distinctId: "some-distinct-id",
                options: new FeatureFlagOptions { OnlyEvaluateLocally = true })
        );
    }

    [Fact] // Ported from PostHog/posthog-python test_get_feature_flag
    public async Task DoesNotCallDecideWhenCanBeEvaluatedLocally()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            { 
              "flags": [
                {
                    "id": 1,
                    "name": "Beta Feature",
                    "key": "beta-feature",
                    "is_simple_flag": false,
                    "active": true,
                    "rollout_percentage": 100,
                    "filters": {
                        "groups": [
                            {
                                "properties": [],
                                "rollout_percentage": 100
                            }
                        ],
                        "multivariate": {
                            "variants": [
                                {"key": "variant-1", "rollout_percentage": 50},
                                {"key": "variant-2", "rollout_percentage": 50}
                            ]
                        }
                    }
                }
              ]
            } 
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetFeatureFlagAsync("beta-feature", distinctId: "some-distinct-Id");

        Assert.Equal(new FeatureFlag(Key: "beta-feature", IsEnabled: true, VariantKey: "variant-1"), result);
    }

    [Fact] // Ported from PostHog/posthog-python test_feature_flag_never_returns_undefined_during_regular_evaluation
    public async Task NeverReturnsNullDuringRegularEvaluation()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        var requestHandler = container.FakeHttpMessageHandler.AddDecideResponse(
            """
            {"featureFlags": {}}
            """
        );
        var secondRequestHandler = container.FakeHttpMessageHandler.AddDecideResponse(
            """
            {"featureFlags": {}}
            """
        );
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            { 
                "flags": [
                {
                    "id": 1,
                    "name": "Beta Feature",
                    "key": "beta-feature",
                    "is_simple_flag": true,
                    "active": true,
                    "filters": {
                        "groups": [
                            {
                                "properties": [],
                                "rollout_percentage": 0
                            }
                        ]
                    }
                }]
            } 
            """
        );
        var client = container.Activate<PostHogClient>();

        // beta-feature resolves to False, so no matter the default, stays False
        Assert.False(await client.GetFeatureFlagAsync("beta-feature", "some-distinct-id"));
        Assert.False(await client.IsFeatureEnabledAsync("beta-feature", "some-distinct-id"));
        Assert.Empty(requestHandler.ReceivedRequests);

        // beta-feature2 falls back to decide, and whatever decide returns is the value
        Assert.False(await client.GetFeatureFlagAsync("beta-feature2", "some-distinct-id"));
        Assert.False(await client.IsFeatureEnabledAsync("beta-feature2", "some-distinct-id"));
        Assert.Single(requestHandler.ReceivedRequests);
        Assert.Single(secondRequestHandler.ReceivedRequests);
    }

    [Fact] // Ported from PostHog/posthog-python test_feature_flag_return_none_when_decide_errors_out
    public async Task ReturnsNullWhenDecideThrowsException()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        var firstRequestHandler =
            container.FakeHttpMessageHandler.AddDecideResponseException(new HttpRequestException());
        var secondRequestHandler =
            container.FakeHttpMessageHandler.AddDecideResponseException(new HttpRequestException());
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse("""{"flags":[]}""");
        var client = container.Activate<PostHogClient>();

        // beta-feature2 falls back to decide, which on error returns None
        Assert.Null(await client.GetFeatureFlagAsync("beta-feature2", "some-distinct-id"));
        Assert.Null(await client.IsFeatureEnabledAsync("beta-feature2", "some-distinct-id"));
        Assert.Single(firstRequestHandler.ReceivedRequests);
        Assert.Single(secondRequestHandler.ReceivedRequests);
    }

    [Fact] // Ported from PostHog/posthog-python test_experience_continuity_flag_not_evaluated_locally
    public async Task ExperienceContinuityFlagNotEvaluatedLocally()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
            {"featureFlags": {"beta-feature": "decide-fallback-value"}}
            """
        );
        var requestHandler = container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
                "flags": [
                    {
                        "id": 1,
                        "name": "Beta Feature",
                        "key": "beta-feature",
                        "is_simple_flag": false,
                        "active": true,
                        "rollout_percentage": 100,
                        "filters": {
                            "groups": [
                                {
                                    "properties": [],
                                    "rollout_percentage": 100
                                }
                            ]
                        },
                        "ensure_experience_continuity": true
                    }
                ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        // decide called always because experience_continuity is set
        Assert.Equal("decide-fallback-value", await client.GetFeatureFlagAsync("beta-feature", "distinct_id"));
        Assert.Single(requestHandler.ReceivedRequests);
    }

    [Fact] // Ported from PostHog/posthog-python test_feature_flags_local_evaluation_None_values
    public async Task LocalEvaluationWithNullValues()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":1,
                     "name":"Beta Feature",
                     "key":"beta-feature",
                     "is_simple_flag":true,
                     "active":true,
                     "filters":{
                        "groups":[
                           {
                              "variant":"None",
                              "properties":[
                                 {
                                    "key":"latestBuildVersion",
                                    "type":"person",
                                    "value":".+",
                                    "operator":"regex"
                                 },
                                 {
                                    "key":"latestBuildVersionMajor",
                                    "type":"person",
                                    "value":"23",
                                    "operator":"gt"
                                 },
                                 {
                                    "key":"latestBuildVersionMinor",
                                    "type":"person",
                                    "value":"31",
                                    "operator":"gt"
                                 },
                                 {
                                    "key":"latestBuildVersionPatch",
                                    "type":"person",
                                    "value":"0",
                                    "operator":"gt"
                                 }
                              ],
                              "rollout_percentage":100
                           }
                        ]
                     }
                  }
               ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        var flag = await client.GetFeatureFlagAsync(
            featureKey: "beta-feature",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                PersonProperties = new()
                {
                    ["latestBuildVersion"] = null,
                    ["latestBuildVersionMajor"] = null,
                    ["latestBuildVersionMinor"] = null,
                    ["latestBuildVersionPatch"] = null
                }
            });

        Assert.False(flag);

        var anotherFlag = await client.GetFeatureFlagAsync(
            featureKey: "beta-feature",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                PersonProperties = new()
                {
                    ["latestBuildVersion"] = "24.32.1",
                    ["latestBuildVersionMajor"] = "24",
                    ["latestBuildVersionMinor"] = "32",
                    ["latestBuildVersionPatch"] = "1"
                }
            });
        Assert.True(anotherFlag);
    }

    [Fact] // Ported from PostHog/posthog-python test_feature_flags_local_evaluation_for_cohorts
    public async Task LocalEvaluationForCohorts()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":2,
                     "name":"Beta Feature",
                     "key":"beta-feature",
                     "is_simple_flag":false,
                     "active":true,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"region",
                                    "operator":"exact",
                                    "value":[
                                       "USA"
                                    ],
                                    "type":"person"
                                 },
                                 {
                                    "key":"id",
                                    "value":98,
                                    "operator":null,
                                    "type":"cohort"
                                 }
                              ],
                              "rollout_percentage":100
                           }
                        ]
                     }
                  }
               ],
               "cohorts":{
                  "98":{
                     "type":"OR",
                     "values":[
                        {
                           "key":"id",
                           "value":1,
                           "type":"cohort"
                        },
                        {
                           "key":"nation",
                           "operator":"exact",
                           "value":[
                              "UK"
                           ],
                           "type":"person"
                        }
                     ]
                  },
                  "1":{
                     "type":"AND",
                     "values":[
                        {
                           "key":"other",
                           "operator":"exact",
                           "value":[
                              "thing"
                           ],
                           "type":"person"
                        }
                     ]
                  }
               }
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        Assert.False(await client.GetFeatureFlagAsync(
            featureKey: "beta-feature",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                PersonProperties = new() { ["region"] = "UK" }
            })
        );
        // even though 'other' property is not present, the cohort should still match since it's an OR condition
        Assert.True(await client.GetFeatureFlagAsync(
            featureKey: "beta-feature",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                PersonProperties = new()
                {
                    ["region"] = "USA",
                    ["nation"] = "UK"
                }
            })
        );
        Assert.True(await client.GetFeatureFlagAsync(
            featureKey: "beta-feature",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                PersonProperties = new()
                {
                    ["region"] = "USA",
                    ["other"] = "thing"
                }
            })
        );
    }

    [Fact] // Ported from PostHog/posthog-python test_feature_flags_local_evaluation_for_negated_cohorts
    public async Task LocalEvaluationForNegatedCohorts()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":2,
                     "name":"Beta Feature",
                     "key":"beta-feature",
                     "is_simple_flag":false,
                     "active":true,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"region",
                                    "operator":"exact",
                                    "value":[
                                       "USA"
                                    ],
                                    "type":"person"
                                 },
                                 {
                                    "key":"id",
                                    "value":98,
                                    "operator":null,
                                    "type":"cohort"
                                 }
                              ],
                              "rollout_percentage":100
                           }
                        ]
                     }
                  }
               ],
               "cohorts":{
                  "98":{
                     "type":"OR",
                     "values":[
                        {
                           "key":"id",
                           "value":1,
                           "type":"cohort"
                        },
                        {
                           "key":"nation",
                           "operator":"exact",
                           "value":[
                              "UK"
                           ],
                           "type":"person"
                        }
                     ]
                  },
                  "1":{
                     "type":"AND",
                     "values":[
                        {
                           "key":"other",
                           "operator":"exact",
                           "value":[
                              "thing"
                           ],
                           "type":"person",
                           "negation":true
                        }
                     ]
                  }
               }
            }
            """
        );
        var decideHandler = container.FakeHttpMessageHandler.AddDecideResponse("{}");
        var client = container.Activate<PostHogClient>();

        Assert.False(await client.GetFeatureFlagAsync(
            featureKey: "beta-feature",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                PersonProperties = new() { ["region"] = "UK" }
            })
        );
        Assert.Empty(decideHandler.ReceivedRequests);

        // even though 'other' property is not present, the cohort should still match since it's an OR condition
        Assert.True(await client.GetFeatureFlagAsync(
            featureKey: "beta-feature",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                PersonProperties = new()
                {
                    ["region"] = "USA",
                    ["nation"] = "UK"
                }
            })
        );
        Assert.Empty(decideHandler.ReceivedRequests);

        // Since 'other' is negated, we return False. Since 'nation' is not present, we can't tell whether the
        // flag should be true or false, so go to decide
        Assert.False(await client.GetFeatureFlagAsync(
            featureKey: "beta-feature",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                PersonProperties = new()
                {
                    ["region"] = "USA",
                    ["other"] = "thing"
                }
            })
        );
        Assert.Single(decideHandler.ReceivedRequests);

        Assert.True(await client.GetFeatureFlagAsync(
            featureKey: "beta-feature",
            distinctId: "some-distinct-id",
            options: new FeatureFlagOptions
            {
                PersonProperties = new()
                {
                    ["region"] = "USA",
                    ["other"] = "thing2"
                }
            })
        );
    }

    [Fact] // Ported from PostHog/posthog-python test_get_feature_flag
    public async Task ReturnsFlag()
    {
        var container = new TestContainer("fake-personal-api-key");
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":1,
                     "name":"Beta Feature",
                     "key":"beta-feature",
                     "is_simple_flag":false,
                     "active":true,
                     "rollout_percentage":100,
                     "filters":{
                        "groups":[
                           {
                              "properties":[],
                              "rollout_percentage":100
                           }
                        ],
                        "multivariate":{
                           "variants":[
                              {
                                 "key":"variant-1",
                                 "rollout_percentage":50
                              },
                              {
                                 "key":"variant-2",
                                 "rollout_percentage":50
                              }
                           ]
                        }
                     }
                  }
               ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        Assert.Equal("variant-1", await client.GetFeatureFlagAsync("beta-feature", "distinct_id"));
    }

    [Fact] // Ported from PostHog/posthog-python test_get_feature_flag_with_variant_overrides
    public async Task GetsFlagWithVariantOverrides()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":1,
                     "name":"Beta Feature",
                     "key":"beta-feature",
                     "is_simple_flag":false,
                     "active":true,
                     "rollout_percentage":100,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"email",
                                    "type":"person",
                                    "value":"test@posthog.com",
                                    "operator":"exact"
                                 }
                              ],
                              "rollout_percentage":100,
                              "variant":"second-variant"
                           },
                           {
                              "rollout_percentage":50,
                              "variant":"first-variant"
                           }
                        ],
                        "multivariate":{
                           "variants":[
                              {
                                 "key":"first-variant",
                                 "name":"First Variant",
                                 "rollout_percentage":50
                              },
                              {
                                 "key":"second-variant",
                                 "name":"Second Variant",
                                 "rollout_percentage":25
                              },
                              {
                                 "key":"third-variant",
                                 "name":"Third Variant",
                                 "rollout_percentage":25
                              }
                           ]
                        }
                     }
                  }
               ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();
        Assert.Equal(
            "second-variant",
            await client.GetFeatureFlagAsync(
                featureKey: "beta-feature",
                distinctId: "test_id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["email"] = "test@posthog.com" }
                }));
        Assert.Equal(
            "first-variant",
            await client.GetFeatureFlagAsync("beta-feature", "example_id"));
    }

    [Fact] // Ported from PostHog/posthog-python test_flag_with_clashing_variant_overrides
    public async Task GetsFlagWithClashingVariantOverrides()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":1,
                     "name":"Beta Feature",
                     "key":"beta-feature",
                     "is_simple_flag":false,
                     "active":true,
                     "rollout_percentage":100,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"email",
                                    "type":"person",
                                    "value":"test@posthog.com",
                                    "operator":"exact"
                                 }
                              ],
                              "rollout_percentage":100,
                              "variant":"second-variant"
                           },
                           {
                              "properties":[
                                 {
                                    "key":"email",
                                    "type":"person",
                                    "value":"test@posthog.com",
                                    "operator":"exact"
                                 }
                              ],
                              "rollout_percentage":100,
                              "variant":"first-variant"
                           },
                           {
                              "rollout_percentage":50,
                              "variant":"first-variant"
                           }
                        ],
                        "multivariate":{
                           "variants":[
                              {
                                 "key":"first-variant",
                                 "name":"First Variant",
                                 "rollout_percentage":50
                              },
                              {
                                 "key":"second-variant",
                                 "name":"Second Variant",
                                 "rollout_percentage":25
                              },
                              {
                                 "key":"third-variant",
                                 "name":"Third Variant",
                                 "rollout_percentage":25
                              }
                           ]
                        }
                     }
                  }
               ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();
        // Because second-variant is first in the group, it takes precedence
        Assert.Equal(
            "second-variant",
            await client.GetFeatureFlagAsync(
                featureKey: "beta-feature",
                distinctId: "test_id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["email"] = "test@posthog.com" }
                }));
        Assert.Equal(
            "second-variant",
            await client.GetFeatureFlagAsync(
                featureKey: "beta-feature",
                distinctId: "example_id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["email"] = "test@posthog.com" }
                }));
    }

    [Fact] // Ported from PostHog/posthog-python test_flag_with_invalid_variant_overrides
    public async Task GetsFlagWithInvalidVariantOverrides()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":1,
                     "name":"Beta Feature",
                     "key":"beta-feature",
                     "is_simple_flag":false,
                     "active":true,
                     "rollout_percentage":100,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"email",
                                    "type":"person",
                                    "value":"test@posthog.com",
                                    "operator":"exact"
                                 }
                              ],
                              "rollout_percentage":100,
                              "variant":"second???"
                           },
                           {
                              "rollout_percentage":50,
                              "variant":"first??"
                           }
                        ],
                        "multivariate":{
                           "variants":[
                              {
                                 "key":"first-variant",
                                 "name":"First Variant",
                                 "rollout_percentage":50
                              },
                              {
                                 "key":"second-variant",
                                 "name":"Second Variant",
                                 "rollout_percentage":25
                              },
                              {
                                 "key":"third-variant",
                                 "name":"Third Variant",
                                 "rollout_percentage":25
                              }
                           ]
                        }
                     }
                  }
               ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();
        Assert.Equal(
            "third-variant",
            await client.GetFeatureFlagAsync(
                featureKey: "beta-feature",
                distinctId: "test_id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["email"] = "test@posthog.com" }
                }));
        Assert.Equal(
            "second-variant",
            await client.GetFeatureFlagAsync(featureKey: "beta-feature", distinctId: "example_id"));
    }

    [Fact] // Ported from PostHog/posthog-python test_flag_with_multiple_variant_overrides
    public async Task GetsFlagWithMultipleVariantOverrides()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":1,
                     "name":"Beta Feature",
                     "key":"beta-feature",
                     "is_simple_flag":false,
                     "active":true,
                     "rollout_percentage":100,
                     "filters":{
                        "groups":[
                           {
                              "rollout_percentage":100
                           },
                           {
                              "properties":[
                                 {
                                    "key":"email",
                                    "type":"person",
                                    "value":"test@posthog.com",
                                    "operator":"exact"
                                 }
                              ],
                              "rollout_percentage":100,
                              "variant":"second-variant"
                           },
                           {
                              "rollout_percentage":50,
                              "variant":"third-variant"
                           }
                        ],
                        "multivariate":{
                           "variants":[
                              {
                                 "key":"first-variant",
                                 "name":"First Variant",
                                 "rollout_percentage":50
                              },
                              {
                                 "key":"second-variant",
                                 "name":"Second Variant",
                                 "rollout_percentage":25
                              },
                              {
                                 "key":"third-variant",
                                 "name":"Third Variant",
                                 "rollout_percentage":25
                              }
                           ]
                        }
                     }
                  }
               ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();
        // The override applies even if the first condition matches all and gives everyone their default group
        Assert.Equal(
            "second-variant",
            await client.GetFeatureFlagAsync(
                featureKey: "beta-feature",
                distinctId: "test_id",
                options: new FeatureFlagOptions
                {
                    PersonProperties = new() { ["email"] = "test@posthog.com" }
                }));
        Assert.Equal(
            "third-variant",
            await client.GetFeatureFlagAsync(featureKey: "beta-feature", distinctId: "example_id"));
        Assert.Equal(
            "second-variant",
            await client.GetFeatureFlagAsync(featureKey: "beta-feature", distinctId: "another_id"));
    }

    [Fact] // Ported from PostHog/posthog-python test_boolean_feature_flag_payloads_local
    public async Task BooleanFeatureFlagPayloadsLocal()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":1,
                     "name":"Beta Feature",
                     "key":"person-flag",
                     "is_simple_flag":true,
                     "active":true,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"region",
                                    "operator":"exact",
                                    "value":[
                                       "USA"
                                    ],
                                    "type":"person"
                                 }
                              ],
                              "rollout_percentage":100
                           }
                        ],
                        "payloads":{
                           "true":"300"
                        }
                     }
                  }
               ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetFeatureFlagAsync("person-flag", "distinct_id", new FeatureFlagOptions
        {
            PersonProperties = new() { ["region"] = "USA" }
        });
        Assert.Equal("300", result?.Payload);
    }

    [Fact] // Ported from PostHog/posthog-python test_boolean_feature_flag_payload_decide
    public async Task BooleanFeatureFlagPayloadsFromDecide()
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
            {"featureFlags": {"person-flag": true}, "featureFlagPayloads": {"person-flag": "300"}}
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetFeatureFlagAsync("person-flag", "distinct_id", new FeatureFlagOptions
        {
            PersonProperties = new() { ["region"] = "USA" }
        });
        Assert.Equal("300", result?.Payload);
    }

    [Fact] // Ported from PostHog/posthog-python test_multivariate_feature_flag_payloads
    public async Task MultivariateFeatureFlagPayloads()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
               "flags":[
                  {
                     "id":1,
                     "name":"Beta Feature",
                     "key":"beta-feature",
                     "is_simple_flag":false,
                     "active":true,
                     "rollout_percentage":100,
                     "filters":{
                        "groups":[
                           {
                              "properties":[
                                 {
                                    "key":"email",
                                    "type":"person",
                                    "value":"test@posthog.com",
                                    "operator":"exact"
                                 }
                              ],
                              "rollout_percentage":100,
                              "variant":"second???"
                           },
                           {
                              "rollout_percentage":50,
                              "variant":"first??"
                           }
                        ],
                        "multivariate":{
                           "variants":[
                              {
                                 "key":"first-variant",
                                 "name":"First Variant",
                                 "rollout_percentage":50
                              },
                              {
                                 "key":"second-variant",
                                 "name":"Second Variant",
                                 "rollout_percentage":25
                              },
                              {
                                 "key":"third-variant",
                                 "name":"Third Variant",
                                 "rollout_percentage":25
                              }
                           ]
                        },
                        "payloads":{
                           "first-variant":"some-payload",
                           "third-variant":"{\"a\":\"json\"}"
                        }
                     }
                  }
               ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetFeatureFlagAsync(
            featureKey: "beta-feature",
            distinctId: "test_id",
            new FeatureFlagOptions
            {
                PersonProperties = new() { ["email"] = "test@posthog.com" }
            });
        Assert.Equal("""{"a":"json"}""", result?.Payload);
    }

    [Fact]
    public async Task ReturnsFalseWhenFlagDoesNotExist()
    {
        var container = new TestContainer();
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddDecideResponse("""{"featureFlags": {}}""");
        var client = container.Activate<PostHogClient>();

        var result = await client.GetFeatureFlagAsync("unknown-flag-key", "distinctId");

        Assert.False(result);
    }

    [Fact]
    public async Task ReturnsStringFlag()
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
            {"featureFlags": {"flag-key": "premium-experience"}}
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetFeatureFlagAsync("flag-key", "distinct-id");

        Assert.NotNull(result);
        Assert.Equal("premium-experience", result.VariantKey);
    }

    [Fact]
    public async Task CapturesFeatureFlagCalledEvent()
    {
        var container = new TestContainer();
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddRepeatedDecideResponse(
            count: 2,
            responseBody: """{"featureFlags": {"flag-key": true}}""");
        var captureRequestHandler = messageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.GetFeatureFlagAsync("flag-key", "a-distinct-id");
        // This call should not call capture because same key, distinct-id, and result.
        await client.GetFeatureFlagAsync("flag-key", "a-distinct-id");

        Assert.NotNull(result);
        Assert.True(result.IsEnabled);
        await client.FlushAsync();
        var received = captureRequestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "api_key": "fake-project-api-key",
                         "historical_migrations": false,
                         "batch": [
                           {
                             "event": "$feature_flag_called",
                             "properties": {
                               "$feature_flag": "flag-key",
                               "$feature_flag_response": true,
                               "locally_evaluated": false,
                               "$feature/flag-key": true,
                               "distinct_id": "a-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{client.Version}}",
                               "$geoip_disable": true
                             },
                             "timestamp": "2024-01-21T19:08:23\u002B00:00"
                           }
                         ]
                       }
                       """, received);
    }

    [Fact]
    public async Task CapturesFeatureFlagCalledEventWithGroupInformation()
    {
        var container = new TestContainer();
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddRepeatedDecideResponse(
            count: 2,
            responseBody: """{"featureFlags": {"flag-key": true}}""");
        var captureRequestHandler = messageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.GetFeatureFlagAsync("flag-key", "a-distinct-id", options: new FeatureFlagOptions
        {
            Groups = [new Group("company", "id:5"), new Group("department", "id:3")]
        });

        Assert.NotNull(result);
        Assert.True(result.IsEnabled);
        await client.FlushAsync();
        var received = captureRequestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "api_key": "fake-project-api-key",
                         "historical_migrations": false,
                         "batch": [
                           {
                             "event": "$feature_flag_called",
                             "properties": {
                               "$feature_flag": "flag-key",
                               "$feature_flag_response": true,
                               "locally_evaluated": false,
                               "$feature/flag-key": true,
                               "distinct_id": "a-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{client.Version}}",
                               "$geoip_disable": true,
                               "$groups": {
                                 "company": "id:5",
                                 "department": "id:3"
                               }
                             },
                             "timestamp": "2024-01-21T19:08:23\u002B00:00"
                           }
                         ]
                       }
                       """, received);
    }


    [Fact]
    public async Task CapturesFeatureFlagCalledEventAgainIfCacheLimitExceededAndIsCompacted()
    {
        var container = new TestContainer(sp =>
        {
            sp.Configure<PostHogOptions>(options =>
            {
                options.ProjectApiKey = "fake-project-api-key";
                options.FeatureFlagSentCacheSizeLimit = 2;
                options.FeatureFlagSentCacheCompactionPercentage = .5; // 50%, or 1 item.
            });
        });
        var timeProvider = container.FakeTimeProvider;
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddRepeatedDecideResponse(
            count: 6,
            responseBody: """{"featureFlags": {"flag-key": "flag-variant-1", "another-flag-key": "flag-variant-2"}}""");
        var captureRequestHandler = messageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        // This is captured and the cache entry will be compacted when the size limit exceeded.
        await client.GetFeatureFlagAsync("flag-key", "a-distinct-id"); // Captured
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await client.GetFeatureFlagAsync("flag-key", "another-distinct-id"); // Captured
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await client.GetFeatureFlagAsync("another-flag-key",
            "another-distinct-id"); // Captured, cache compaction will occur after this.
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await client.GetFeatureFlagAsync("another-flag-key", "another-distinct-id"); // Cache hit, not captured.
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await client.GetFeatureFlagAsync("flag-key", "a-distinct-id"); // Captured because cache limit exceeded.

        await client.FlushAsync();
        var received = captureRequestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "api_key": "fake-project-api-key",
                         "historical_migrations": false,
                         "batch": [
                           {
                             "event": "$feature_flag_called",
                             "properties": {
                               "$feature_flag": "flag-key",
                               "$feature_flag_response": "flag-variant-1",
                               "locally_evaluated": false,
                               "$feature/flag-key": "flag-variant-1",
                               "distinct_id": "a-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{client.Version}}",
                               "$geoip_disable": true
                             },
                             "timestamp": "2024-01-21T19:08:23\u002B00:00"
                           },
                           {
                             "event": "$feature_flag_called",
                             "properties": {
                               "$feature_flag": "flag-key",
                               "$feature_flag_response": "flag-variant-1",
                               "locally_evaluated": false,
                               "$feature/flag-key": "flag-variant-1",
                               "distinct_id": "another-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{client.Version}}",
                               "$geoip_disable": true
                             },
                             "timestamp": "2024-01-21T19:08:24\u002B00:00"
                           },
                           {
                             "event": "$feature_flag_called",
                             "properties": {
                               "$feature_flag": "another-flag-key",
                               "$feature_flag_response": "flag-variant-2",
                               "locally_evaluated": false,
                               "$feature/another-flag-key": "flag-variant-2",
                               "distinct_id": "another-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{client.Version}}",
                               "$geoip_disable": true
                             },
                             "timestamp": "2024-01-21T19:08:25\u002B00:00"
                           },
                           {
                             "event": "$feature_flag_called",
                             "properties": {
                               "$feature_flag": "flag-key",
                               "$feature_flag_response": "flag-variant-1",
                               "locally_evaluated": false,
                               "$feature/flag-key": "flag-variant-1",
                               "distinct_id": "a-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{client.Version}}",
                               "$geoip_disable": true
                             },
                             "timestamp": "2024-01-21T19:08:27\u002B00:00"
                           }
                         ]
                       }
                       """, received);
    }

    [Fact]
    public async Task CapturesFeatureFlagCalledEventAgainIfCacheSlidingWindowExpirationOccurs()
    {
        var container = new TestContainer(sp => sp.AddSingleton<IOptions<PostHogOptions>>(new PostHogOptions
        {
            ProjectApiKey = "test-api-key",
            FeatureFlagSentCacheSizeLimit = 20,
            FeatureFlagSentCacheSlidingExpiration = TimeSpan.FromSeconds(3)
        }));
        var timeProvider = container.FakeTimeProvider;
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddRepeatedDecideResponse(
            count: 6,
            """{"featureFlags": {"flag-key": "flag-variant-1", "another-flag-key": "flag-variant-2" } }""");
        var captureRequestHandler = messageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        await client.GetFeatureFlagAsync("flag-key", "a-distinct-id"); // Captured.
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await client.GetFeatureFlagAsync("flag-key", "another-distinct-id"); // Captured
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await client.GetFeatureFlagAsync("another-flag-key", "another-distinct-id"); // Captured
        timeProvider.Advance(TimeSpan.FromSeconds(1)); // Sliding time window expires for first entry.
        await client.GetFeatureFlagAsync("another-flag-key", "another-distinct-id"); // Cache hit, not captured.
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await client.GetFeatureFlagAsync("flag-key", "a-distinct-id"); // Captured.

        await client.FlushAsync();
        var received = captureRequestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "api_key": "test-api-key",
                         "historical_migrations": false,
                         "batch": [
                           {
                             "event": "$feature_flag_called",
                             "properties": {
                               "$feature_flag": "flag-key",
                               "$feature_flag_response": "flag-variant-1",
                               "locally_evaluated": false,
                               "$feature/flag-key": "flag-variant-1",
                               "distinct_id": "a-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{client.Version}}",
                               "$geoip_disable": true
                             },
                             "timestamp": "2024-01-21T19:08:23\u002B00:00"
                           },
                           {
                             "event": "$feature_flag_called",
                             "properties": {
                               "$feature_flag": "flag-key",
                               "$feature_flag_response": "flag-variant-1",
                               "locally_evaluated": false,
                               "$feature/flag-key": "flag-variant-1",
                               "distinct_id": "another-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{client.Version}}",
                               "$geoip_disable": true
                             },
                             "timestamp": "2024-01-21T19:08:24\u002B00:00"
                           },
                           {
                             "event": "$feature_flag_called",
                             "properties": {
                               "$feature_flag": "another-flag-key",
                               "$feature_flag_response": "flag-variant-2",
                               "locally_evaluated": false,
                               "$feature/another-flag-key": "flag-variant-2",
                               "distinct_id": "another-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{client.Version}}",
                               "$geoip_disable": true
                             },
                             "timestamp": "2024-01-21T19:08:25\u002B00:00"
                           },
                           {
                             "event": "$feature_flag_called",
                             "properties": {
                               "$feature_flag": "flag-key",
                               "$feature_flag_response": "flag-variant-1",
                               "locally_evaluated": false,
                               "$feature/flag-key": "flag-variant-1",
                               "distinct_id": "a-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{client.Version}}",
                               "$geoip_disable": true
                             },
                             "timestamp": "2024-01-21T19:08:27\u002B00:00"
                           }
                         ]
                       }
                       """, received);
    }

    [Fact]
    public async Task DoesNotCaptureFeatureFlagCalledEventWhenSendFeatureFlagsFalse()
    {
        var container = new TestContainer();
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddRepeatedDecideResponse(
            count: 4,
            responseBodyFunc: count => $$"""{"featureFlags": {"flag-key": "feature-value-{{count}}"} }""");
        var captureRequestHandler = messageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        await client.GetFeatureFlagAsync(featureKey: "flag-key",
            distinctId: "a-distinct-id", options: new FeatureFlagOptions { SendFeatureFlagEvents = false });

        await client.FlushAsync();
        Assert.Empty(captureRequestHandler.ReceivedRequests);
    }
}

public class TheGetAllFeatureFlagsAsyncMethod
{
    [Fact] // Ported from PostHog/posthog-python test_get_all_flags_with_fallback
    public async Task RetrievesAllFlagsWithFallback()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
                {
                   "featureFlags":{
                      "beta-feature":"variant-1",
                      "beta-feature2":"variant-2",
                      "disabled-feature":false
                   }
                }
                """
        );
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
                {
                  "flags":[
                     {
                        "id":1,
                        "name":"Beta Feature",
                        "key":"beta-feature",
                        "is_simple_flag":false,
                        "active":true,
                        "rollout_percentage":100,
                        "filters":{
                           "groups":[
                              {
                                 "properties":[],
                                 "rollout_percentage":100
                              }
                           ]
                        }
                     },
                     {
                        "id":2,
                        "name":"Beta Feature",
                        "key":"disabled-feature",
                        "is_simple_flag":false,
                        "active":true,
                        "filters":{
                           "groups":[
                              {
                                 "properties":[],
                                 "rollout_percentage":0
                              }
                           ]
                        }
                     },
                     {
                        "id":3,
                        "name":"Beta Feature",
                        "key":"beta-feature2",
                        "is_simple_flag":false,
                        "active":true,
                        "filters":{
                           "groups":[
                              {
                                 "properties":[
                                    {
                                       "key":"country",
                                       "value":"US"
                                    }
                                 ],
                                 "rollout_percentage":0
                              }
                           ]
                        }
                     }
                  ]
                }
                """
        );
        var client = container.Activate<PostHogClient>();

        // We expect a fallback because no properties were supplied.
        var results = await client.GetAllFeatureFlagsAsync(distinctId: "some-distinct-id");

        // beta-feature value overridden by /decide
        Assert.Equal(new Dictionary<string, FeatureFlag>
        {
            ["beta-feature"] = new(Key: "beta-feature", IsEnabled: true, VariantKey: "variant-1"),
            ["beta-feature2"] = new(Key: "beta-feature2", IsEnabled: true, VariantKey: "variant-2"),
            ["disabled-feature"] = new(Key: "disabled-feature", IsEnabled: false)
        }, results);
    }

    [Fact] // Ported from PostHog/posthog-python test_get_all_flags_and_payloads_with_fallback
    public async Task RetrievesAllFlagsAndPayloadsWithFallback()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        var captureRequestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var decideRequestHandler = container.FakeHttpMessageHandler.AddDecideResponse(
            """
                {
                   "featureFlags":{
                      "beta-feature":"variant-1",
                      "beta-feature2":"variant-2",
                      "disabled-feature":false
                   },
                   "featureFlagPayloads": {"beta-feature": "100", "beta-feature2": "300"}
                }
                """
        );
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
                {
                   "flags":[
                      {
                         "id":1,
                         "name":"Beta Feature",
                         "key":"beta-feature",
                         "is_simple_flag":false,
                         "active":true,
                         "rollout_percentage":100,
                         "filters":{
                            "groups":[
                               {
                                  "properties":[
                                     
                                  ],
                                  "rollout_percentage":100
                               }
                            ],
                            "payloads":{
                               "true":"some-payload"
                            }
                         }
                      },
                      {
                         "id":2,
                         "name":"Beta Feature",
                         "key":"disabled-feature",
                         "is_simple_flag":false,
                         "active":true,
                         "filters":{
                            "groups":[
                               {
                                  "properties":[
                                     
                                  ],
                                  "rollout_percentage":0
                               }
                            ],
                            "payloads":{
                               "true":"another-payload"
                            }
                         }
                      },
                      {
                         "id":3,
                         "name":"Beta Feature",
                         "key":"beta-feature2",
                         "is_simple_flag":false,
                         "active":true,
                         "filters":{
                            "groups":[
                               {
                                  "properties":[
                                     {
                                        "key":"country",
                                        "value":"US"
                                     }
                                  ],
                                  "rollout_percentage":0
                               }
                            ],
                            "payloads":{
                               "true":"payload-3"
                            }
                         }
                      }
                   ]
                }
                """
        );
        var client = container.Activate<PostHogClient>();

        // We expect a fallback because no properties were supplied.
        var results = await client.GetAllFeatureFlagsAsync(distinctId: "some-distinct-id");

        // beta-feature value overridden by /decide
        Assert.Equal(new Dictionary<string, FeatureFlag>
        {
            ["beta-feature"] = new(Key: "beta-feature", IsEnabled: true, VariantKey: "variant-1", Payload: "100"),
            ["beta-feature2"] = new(Key: "beta-feature2", IsEnabled: true, VariantKey: "variant-2", Payload: "300"),
            ["disabled-feature"] = new(Key: "disabled-feature", IsEnabled: false)
        }, results);
        Assert.Single(decideRequestHandler.ReceivedRequests);
        Assert.Empty(captureRequestHandler.ReceivedRequests);
    }

    [Fact] // Ported from PostHog/posthog-python test_get_all_flags_with_fallback_empty_local_flags
    public async Task RetrievesAllFlagsWithFallbackAndEmptyLocalFlags()
    {
        var container = new TestContainer(personalApiKey: "fake-person");
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
                {"featureFlags": {"beta-feature": "variant-1", "beta-feature2": "variant-2"}}
                """
        );
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
                {"flags": []}
                """
        );
        var client = container.Activate<PostHogClient>();

        // Beta feature overridden by /decide
        var result = await client.GetAllFeatureFlagsAsync("some-distinct-id");

        Assert.Equal(new Dictionary<string, FeatureFlag>
        {
            ["beta-feature"] = new(Key: "beta-feature", IsEnabled: true, VariantKey: "variant-1"),
            ["beta-feature2"] = new(Key: "beta-feature2", IsEnabled: true, VariantKey: "variant-2")
        }, result);
    }

    [Fact] // Ported from PostHog/posthog-python test_get_all_flags_and_payloads_with_fallback_empty_local_flags
    public async Task RetrievesAllFlagsAndPayloadsWithFallbackAndEmptyLocalFlags()
    {
        var container = new TestContainer(personalApiKey: "fake-person");
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
                {
                    "featureFlags": {"beta-feature": "variant-1", "beta-feature2": "variant-2"},
                    "featureFlagPayloads": {"beta-feature": "100", "beta-feature2": "300"}
                }
                """
        );
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
                {"flags": []}
                """
        );
        var client = container.Activate<PostHogClient>();

        // Beta feature overridden by /decide
        var result = await client.GetAllFeatureFlagsAsync("some-distinct-id");

        Assert.Equal(new Dictionary<string, FeatureFlag>
        {
            ["beta-feature"] = new(Key: "beta-feature", IsEnabled: true, VariantKey: "variant-1", Payload: "100"),
            ["beta-feature2"] = new(Key: "beta-feature2", IsEnabled: true, VariantKey: "variant-2", Payload: "300")
        }, result);
    }

    [Fact] // Ported from PostHog/posthog-python test_get_all_flags_with_no_fallback
    public async Task RetrievesAllFlagsWithNoFallback()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
                {
                  "flags":[
                     {
                        "id":1,
                        "name":"Beta Feature",
                        "key":"beta-feature",
                        "is_simple_flag":false,
                        "active":true,
                        "rollout_percentage":100,
                        "filters":{
                           "groups":[
                              {
                                 "properties":[],
                                 "rollout_percentage":100
                              }
                           ]
                        }
                     },
                     {
                        "id":2,
                        "name":"Beta Feature",
                        "key":"disabled-feature",
                        "is_simple_flag":false,
                        "active":true,
                        "filters":{
                           "groups":[
                              {
                                 "properties":[],
                                 "rollout_percentage":0
                              }
                           ]
                        }
                     }
                  ]
                }
                """
        );
        var client = container.Activate<PostHogClient>();

        var results = await client.GetAllFeatureFlagsAsync("some-distinct-id");

        Assert.Equal(new Dictionary<string, FeatureFlag>
        {
            ["beta-feature"] = new("beta-feature", true),
            ["disabled-feature"] = new("disabled-feature", false)
        }, results);
    }

    [Fact] // Ported from PostHog/posthog-python test_get_all_flags_and_payloads_with_no_fallback
    public async Task RetrievesAllFlagsAndPayloadsWithNoFallback()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
                {
                   "flags":[
                      {
                         "id":1,
                         "name":"Beta Feature",
                         "key":"beta-feature",
                         "is_simple_flag":false,
                         "active":true,
                         "rollout_percentage":100,
                         "filters":{
                            "groups":[
                               {
                                  "properties":[
                                     
                                  ],
                                  "rollout_percentage":100
                               }
                            ],
                            "payloads":{
                               "true":"new"
                            }
                         }
                      },
                      {
                         "id":2,
                         "name":"Beta Feature",
                         "key":"disabled-feature",
                         "is_simple_flag":false,
                         "active":true,
                         "filters":{
                            "groups":[
                               {
                                  "properties":[
                                     
                                  ],
                                  "rollout_percentage":0
                               }
                            ],
                            "payloads":{
                               "true":"some-payload"
                            }
                         }
                      }
                   ]
                }
                """
        );
        var client = container.Activate<PostHogClient>();

        var results = await client.GetAllFeatureFlagsAsync("some-distinct-id");

        Assert.Equal(new Dictionary<string, FeatureFlag>
        {
            ["beta-feature"] = new("beta-feature", true, Payload: "new"),
            ["disabled-feature"] = new("disabled-feature", false)
        }, results);
    }

    [Fact] // Ported from PostHog/posthog-python test_get_all_flags_with_fallback_but_only_local_evaluation_set
    public async Task RetrievesAllFlagsWithFallbackButOnlyLocalEvaluationSet()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        var decideHandler = container.FakeHttpMessageHandler.AddDecideResponse(
            """
                {
                   "featureFlags":{
                      "beta-feature":"variant-1",
                      "beta-feature2":"variant-2"
                   }
                }
                """
        );
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
                {
                  "flags":[
                     {
                        "id":1,
                        "name":"Beta Feature",
                        "key":"beta-feature",
                        "is_simple_flag":false,
                        "active":true,
                        "rollout_percentage":100,
                        "filters":{
                           "groups":[
                              {
                                 "properties":[],
                                 "rollout_percentage":100
                              }
                           ]
                        }
                     },
                     {
                        "id":2,
                        "name":"Beta Feature",
                        "key":"disabled-feature",
                        "is_simple_flag":false,
                        "active":true,
                        "filters":{
                           "groups":[
                              {
                                 "properties":[],
                                 "rollout_percentage":0
                              }
                           ]
                        }
                     },
                     {
                        "id":3,
                        "name":"Beta Feature",
                        "key":"beta-feature2",
                        "is_simple_flag":false,
                        "active":true,
                        "filters":{
                           "groups":[
                              {
                                 "properties":[
                                    {
                                       "key":"country",
                                       "value":"US"
                                    }
                                 ],
                                 "rollout_percentage":0
                              }
                           ]
                        }
                     }
                  ]
                }
                """
        );
        var client = container.Activate<PostHogClient>();

        // We expect a fallback because no properties were supplied.
        var results = await client.GetAllFeatureFlagsAsync(
            distinctId: "some-distinct-id",
            options: new AllFeatureFlagsOptions { OnlyEvaluateLocally = true });

        // beta-feature2 has no value
        Assert.Equal(new Dictionary<string, FeatureFlag>
        {
            ["beta-feature"] = new(Key: "beta-feature", IsEnabled: true),
            ["disabled-feature"] = new(Key: "disabled-feature", IsEnabled: false)
        }, results);
        Assert.Empty(decideHandler.ReceivedRequests);
    }

    [Fact] // Ported from PostHog/posthog-python test_get_all_flags_and_payloads_with_fallback_but_only_local_evaluation_set
    public async Task RetrievesAllFlagsAndPayloadsWithFallbackButOnlyLocalEvaluationSet()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        var decideHandler = container.FakeHttpMessageHandler.AddDecideResponse(
            """
                {
                    "featureFlags": {"beta-feature": "variant-1", "beta-feature2": "variant-2"},
                    "featureFlagPayloads": {"beta-feature": "100", "beta-feature2": "300"}
                }
                """
        );
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
                {
                  "flags":[
                     {
                        "id":1,
                        "name":"Beta Feature",
                        "key":"beta-feature",
                        "is_simple_flag":false,
                        "active":true,
                        "rollout_percentage":100,
                        "filters":{
                           "groups":[
                              {
                                 "properties":[],
                                 "rollout_percentage":100
                              }
                           ],
                           "payloads": {
                                "true": "some-payload"
                           }
                        }
                     },
                     {
                        "id":2,
                        "name":"Beta Feature",
                        "key":"disabled-feature",
                        "is_simple_flag":false,
                        "active":true,
                        "filters":{
                           "groups":[
                              {
                                 "properties":[],
                                 "rollout_percentage":0
                              }
                           ],
                           "payloads": {
                                "true": "another-payload"
                           }
                        }
                     },
                     {
                        "id":3,
                        "name":"Beta Feature",
                        "key":"beta-feature2",
                        "is_simple_flag":false,
                        "active":true,
                        "filters":{
                           "groups":[
                              {
                                 "properties":[
                                    {
                                       "key":"country",
                                       "value":"US"
                                    }
                                 ],
                                 "rollout_percentage":0
                              }
                           ],
                           "payloads": {
                                "true": "payload-3"
                           }
                        }
                     }
                  ]
                }
                """
        );
        var client = container.Activate<PostHogClient>();

        // We expect a fallback because no properties were supplied.
        var results = await client.GetAllFeatureFlagsAsync(
            distinctId: "some-distinct-id",
            options: new AllFeatureFlagsOptions { OnlyEvaluateLocally = true });

        // beta-feature2 has no value
        Assert.Equal(new Dictionary<string, FeatureFlag>
        {
            ["beta-feature"] = new(Key: "beta-feature", IsEnabled: true, Payload: "some-payload"),
            ["disabled-feature"] = new(Key: "disabled-feature", IsEnabled: false)
        }, results);
        Assert.Empty(decideHandler.ReceivedRequests);
    }

    [Fact] // Ported from PostHog/posthog-python test_compute_inactive_flags_locally
    public async Task ComputesInactiveFlagsLocally()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
                {
                   "flags":[
                      {
                         "id":1,
                         "name":"Beta Feature",
                         "key":"beta-feature",
                         "is_simple_flag":false,
                         "active":true,
                         "rollout_percentage":100,
                         "filters":{
                            "groups":[
                               {
                                  "properties":[
                                     
                                  ],
                                  "rollout_percentage":100
                               }
                            ]
                         }
                      },
                      {
                         "id":2,
                         "name":"Beta Feature",
                         "key":"disabled-feature",
                         "is_simple_flag":false,
                         "active":true,
                         "filters":{
                            "groups":[
                               {
                                  "properties":[
                                     
                                  ],
                                  "rollout_percentage":0
                               }
                            ]
                         }
                      }
                   ]
                }
                """
        );
        var client = container.Activate<PostHogClient>();

        Assert.Equal(new Dictionary<string, FeatureFlag>
        {
            ["beta-feature"] = new(Key: "beta-feature", IsEnabled: true),
            ["disabled-feature"] = new(Key: "disabled-feature", IsEnabled: false)
        }, await client.GetAllFeatureFlagsAsync("some-distinct-id"));

        // Now, after a poll interval, flag 1 is inactive, and flag 2 rollout is set to 100%.
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
                {
                   "flags":[
                      {
                         "id":1,
                         "name":"Beta Feature",
                         "key":"beta-feature",
                         "is_simple_flag":false,
                         "active":false,
                         "rollout_percentage":100,
                         "filters":{
                            "groups":[
                               {
                                  "properties":[
                                     
                                  ],
                                  "rollout_percentage":100
                               }
                            ]
                         }
                      },
                      {
                         "id":2,
                         "name":"Beta Feature",
                         "key":"disabled-feature",
                         "is_simple_flag":false,
                         "active":true,
                         "filters":{
                            "groups":[
                               {
                                  "properties":[
                                     
                                  ],
                                  "rollout_percentage":100
                               }
                            ]
                         }
                      }
                   ]
                }
                """
        );
        container.FakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(1); // Cede execution to thread that's loading the new flags.

        Assert.Equal(new Dictionary<string, FeatureFlag>
        {
            ["beta-feature"] = new(Key: "beta-feature", IsEnabled: false),
            ["disabled-feature"] = new(Key: "disabled-feature", IsEnabled: true)
        }, await client.GetAllFeatureFlagsAsync("some-distinct-id"));
    }

    [Fact]
    public async Task UpdatesFeatureFlagsOnTimer()
    {
        var container = new TestContainer(services =>
        {
            services.Configure<PostHogOptions>(options =>
            {
                options.ProjectApiKey = "fake-project-api-key";
                options.PersonalApiKey = "fake-personal-api-key";
                options.FeatureFlagPollInterval = TimeSpan.FromSeconds(30);
            });
        });
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
                "flags": [
                    {
                        "id": 123,
                        "team_id": 456,
                        "name": "Flag Name",
                        "key": "flag-key",
                        "filters": null
                    }
                ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetAllFeatureFlagsAsync("distinct_id");

        Assert.NotNull(result);
        var flag = Assert.Single(result.Values);
        Assert.Equal("flag-key", flag.Key);

        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
                "flags": [
                    {
                        "id": 123,
                        "team_id": 456,
                        "name": "Flag Name",
                        "key": "flag-key-2",
                        "filters": null
                    }
                ]
            }
            """
        );
        container.FakeTimeProvider.Advance(TimeSpan.FromSeconds(31));
        await Task.Delay(100); // Cede execution to thread that's loading the new flags.

        var newResult = await client.GetAllFeatureFlagsAsync("distinct_id");

        Assert.NotNull(newResult);
        var newFlag = Assert.Single(newResult.Values);
        Assert.Equal("flag-key-2", newFlag.Key);
    }
}
