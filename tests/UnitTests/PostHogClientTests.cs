using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostHog;
using PostHog.Config;
using PostHog.Features;
using PostHog.Versioning;
using UnitTests.Fakes;

#pragma warning disable CA2000
namespace PostHogClientTests;

public class TheCaptureEventMethod
{
    [Fact]
    public async Task SendsBatchToCaptureEndpoint()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var result = client.Capture("some-distinct-id", "some_event");

        Assert.True(result);
        await client.FlushAsync();
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "api_key": "fake-project-api-key",
                         "historical_migrations": false,
                         "batch": [
                           {
                             "event": "some_event",
                             "properties": {
                               "distinct_id": "some-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{VersionConstants.Version}}",
                               "$geoip_disable": true
                             },
                             "timestamp": "2024-01-21T19:08:23\u002B00:00"
                           }
                         ]
                       }
                       """, received);
    }

    [Fact] // Ported from PostHog/posthog-python test_groups_capture
    public async Task CapturesGroups()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var result = client.Capture(
            distinctId: "some-distinct-id",
            eventName: "some_event",
            groups: [
                new Group("company", "id:5"),
                new Group("department", "engineering")
            ]);

        Assert.True(result);
        await client.FlushAsync();
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "api_key": "fake-project-api-key",
                         "historical_migrations": false,
                         "batch": [
                           {
                             "event": "some_event",
                             "properties": {
                               "distinct_id": "some-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{VersionConstants.Version}}",
                               "$geoip_disable": true,
                               "$groups": {
                                 "company": "id:5",
                                 "department": "engineering"
                               }
                             },
                             "timestamp": "2024-01-21T19:08:23\u002B00:00"
                           }
                         ]
                       }
                       """, received);
    }

    [Fact] // Ported from PostHog/posthog-python test_basic_super_properties
    public async Task SendsSuperPropertiesToEndpoint()
    {
        var container = new TestContainer(sp =>
        {
            sp.AddSingleton<IOptions<PostHogOptions>>(new PostHogOptions
            {
                ProjectApiKey = "fake-project-api-key",
                SuperProperties = new Dictionary<string, object> { ["source"] = "repo-name" }
            });
        });
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var result = client.Capture("some-distinct-id", "some_event");

        Assert.True(result);
        await client.FlushAsync();
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "api_key": "fake-project-api-key",
                         "historical_migrations": false,
                         "batch": [
                           {
                             "event": "some_event",
                             "properties": {
                               "distinct_id": "some-distinct-id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{VersionConstants.Version}}",
                               "$geoip_disable": true,
                               "source": "repo-name"
                             },
                             "timestamp": "2024-01-21T19:08:23\u002B00:00"
                           }
                         ]
                       }
                       """, received);
    }

    [Fact] // Ported from PostHog/posthog-python test_basic_capture_with_feature_flags
    public async Task SendsFeatureFlags()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
          {"featureFlags": {"beta-feature": "random-variant", "another-feature": "another-variant", "false-flag": false}}
          """
        );
        var client = container.Activate<PostHogClient>();

        var result = client.Capture("some-distinct-id", "dotnet test event", sendFeatureFlags: true);

        Assert.True(result);
        await client.FlushAsync();
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "event": "dotnet test event",
                           "properties": {
                             "distinct_id": "some-distinct-id",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$feature/beta-feature": "random-variant",
                             "$feature/another-feature": "another-variant",
                             "$feature/false-flag": false,
                             "$active_feature_flags": [
                               "beta-feature",
                               "another-feature"
                             ]
                           },
                           "timestamp": "2024-01-21T19:08:23\u002B00:00"
                         }
                       ]
                     }
                     """, received);
    }

    [Fact] // Ported from PostHog/posthog-python test_basic_capture_with_locally_evaluated_feature_flags
    public async Task SendsLocallyEvaluatedFeatureFlags()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var firstRequestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var secondRequestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
                "flags": [{
                    "id": 1,
                    "name": "Beta Feature",
                    "key": "beta-feature-local",
                    "active": true,
                    "rollout_percentage": 100,
                    "filters": {
                        "groups": [
                            {
                                "properties": [
                                    {"key": "email", "type": "person", "value": "test@posthog.com", "operator": "exact"}
                                ],
                                "rollout_percentage": 100
                            },
                            {
                                "rollout_percentage": 50
                            }
                        ],
                        "multivariate": {
                            "variants": [
                                {"key": "first-variant", "name": "First Variant", "rollout_percentage": 50},
                                {"key": "second-variant", "name": "Second Variant", "rollout_percentage": 25},
                                {"key": "third-variant", "name": "Third Variant", "rollout_percentage": 25}
                            ]
                        },
                        "payloads": {"first-variant": "some-payload", "third-variant": "{\"a\": \"json\"}"}
                    }
                },
                {
                    "id": 2,
                    "name": "Beta Feature",
                    "key": "person-flag",
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
                                    }
                                ],
                                "rollout_percentage": 100
                            }
                        ],
                        "payloads": {"true": "300"}
                    }
                },
                {
                    "id": 3,
                    "name": "Beta Feature",
                    "key": "false-flag",
                    "active": true,
                    "filters": {
                        "groups": [
                            {
                                "properties": [],
                                "rollout_percentage": 0
                            }
                        ],
                        "payloads": {"true": "300"}
                    }
                }]
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        // Call it without pre-loading flags.
        var firstCaptureResult = client.Capture("distinct_id", "dotnet test event");

        Assert.True(firstCaptureResult);
        await client.FlushAsync();
        var firstRequestBody = firstRequestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "event": "dotnet test event",
                           "properties": {
                             "distinct_id": "distinct_id",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true
                           },
                           "timestamp": "2024-01-21T19:08:23\u002B00:00"
                         }
                       ]
                     }
                     """, firstRequestBody);

        // Load the feature flags
        await client.GetAllFeatureFlagsAsync("distinct_id", options: new AllFeatureFlagsOptions { OnlyEvaluateLocally = true });
        var secondCaptureResult = client.Capture("distinct_id", "dotnet test event");

        await client.FlushAsync();
        var secondRequestBody = secondRequestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "api_key": "fake-project-api-key",
                         "historical_migrations": false,
                         "batch": [
                           {
                             "event": "dotnet test event",
                             "properties": {
                               "distinct_id": "distinct_id",
                               "$lib": "posthog-dotnet",
                               "$lib_version": "{{VersionConstants.Version}}",
                               "$geoip_disable": true,
                               "$feature/beta-feature-local": "third-variant",
                               "$feature/false-flag": false,
                               "$active_feature_flags": [
                                 "beta-feature-local"
                               ]
                             },
                             "timestamp": "2024-01-21T19:08:23\u002B00:00"
                           }
                         ]
                       }
                       """, secondRequestBody);
        Assert.True(secondCaptureResult);
    }

    [Fact]
    public async Task UsesAuthenticatedHttpClientForLocalEvaluationFlags()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
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
                 }
              ]
            }
            """
        );
        var client = container.Activate<PostHogClient>();

        await client.GetAllFeatureFlagsAsync("some-distinct-id");

        var received = requestHandler.ReceivedRequest;
        Assert.NotNull(received.Headers.Authorization);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "fake-personal-api-key"), received.Headers.Authorization);
    }
}

public class TheIdentifyPersonAsyncMethod
{
    [Fact] // Similar to PostHog/posthog-python test_basic_identify
    public async Task SendsCorrectPayload()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddCaptureResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.IdentifyAsync("some-distinct-id");

        Assert.Equal(1, result.Status);
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "event": "$identify",
                         "distinct_id": "some-distinct-id",
                         "properties": {
                           "$lib": "posthog-dotnet",
                           "$lib_version": "{{VersionConstants.Version}}",
                           "$geoip_disable": true
                         },
                         "api_key": "fake-project-api-key",
                         "timestamp": "2024-01-21T19:08:23\u002B00:00"
                       }
                       """, received);
    }

    [Fact]
    public async Task SendsCorrectPayloadWithPersonProperties()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddCaptureResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.IdentifyAsync(
            distinctId: "some-distinct-id",
            email: "wildling-lover@example.com",
            name: "Jon Snow",
            personPropertiesToSet: new() { ["age"] = 36 },
            personPropertiesToSetOnce: new() { ["join_date"] = "2024-01-21" },
            CancellationToken.None);

        Assert.Equal(1, result.Status);
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "event": "$identify",
                         "distinct_id": "some-distinct-id",
                         "properties": {
                           "$set": {
                             "age": 36,
                             "email": "wildling-lover@example.com",
                             "name": "Jon Snow"
                           },
                           "$set_once": {
                             "join_date": "2024-01-21"
                           },
                           "$lib": "posthog-dotnet",
                           "$lib_version": "{{VersionConstants.Version}}",
                           "$geoip_disable": true
                         },
                         "api_key": "fake-project-api-key",
                         "timestamp": "2024-01-21T19:08:23\u002B00:00"
                       }
                       """, received);
    }

    [Fact] // Ported from PostHog/posthog-python test_basic_super_properties
    public async Task SendsCorrectPayloadWithSuperProperties()
    {
        var container = new TestContainer(sp =>
        {
            sp.AddSingleton<IOptions<PostHogOptions>>(new PostHogOptions
            {
                ProjectApiKey = "fake-project-api-key",
                SuperProperties = new Dictionary<string, object> { ["source"] = "repo-name" }
            });
        });
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddCaptureResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.IdentifyAsync("some-distinct-id");

        Assert.Equal(1, result.Status);
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "event": "$identify",
                         "distinct_id": "some-distinct-id",
                         "properties": {
                           "$lib": "posthog-dotnet",
                           "$lib_version": "{{VersionConstants.Version}}",
                           "$geoip_disable": true,
                           "source": "repo-name"
                         },
                         "api_key": "fake-project-api-key",
                         "timestamp": "2024-01-21T19:08:23\u002B00:00"
                       }
                       """, received);
    }
}

public class TheIdentifyGroupAsyncMethod
{
    [Fact] // Ported from PostHog/posthog-python test_basic_group_identify
    public async Task SendsCorrectPayload()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddCaptureResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.GroupIdentifyAsync(type: "organization", key: "id:5", "PostHog");

        Assert.Equal(1, result.Status);
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "event": "$groupidentify",
                         "distinct_id": "$organization_id:5",
                         "properties": {
                           "$group_type": "organization",
                           "$group_key": "id:5",
                           "$group_set": {
                             "name": "PostHog"
                           },
                           "$lib": "posthog-dotnet",
                           "$lib_version": "{{VersionConstants.Version}}",
                           "$geoip_disable": true
                         },
                         "api_key": "fake-project-api-key",
                         "timestamp": "2024-01-21T19:08:23\u002B00:00"
                       }
                       """, received);
    }
}
