using Microsoft.FeatureManagement;
using PostHog.Config;
using PostHog.FeatureManagement;
using PostHogVariantFeatureManagerTests;
using UnitTests.Fakes;
using UnitTests.Library;

namespace PostHogFeatureDefinitionProviderTests;

public class TheGetAllFeatureDefinitionsAsyncMethod
{
    [Fact]
    public async Task ReturnsAllActiveFeatures()
    {
        var container = new TestContainer(sp =>
        {
            var builder = new PostHogConfigurationBuilder(sp);
            builder.UseFeatureManagement<FakePostHogFeatureFlagContextProvider>();
            builder.PostConfigure(o => o.SecretKey = "fake-personal-api-key");
        });
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
              "flags":[
                 {
                    "id":1,
                    "name":"Beta Feature",
                    "key":"beta-feature",
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
                    "name":"Alpha Feature",
                    "key":"alpha-feature",
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
        var provider = container.Activate<PostHogFeatureDefinitionProvider>();

        var features = await provider.GetAllFeatureDefinitionsAsync().ToListAsync();

        Assert.Collection(
            features,
                f => Assert.Equal("beta-feature", f.Name),
                f => Assert.Equal("alpha-feature", f.Name)
        );
    }
}

public class TheGetAllFeatureDefinitionsAsyncMethodFallback
{
    [Fact]
    public async Task ReturnsFlagsFromFlagsEndpointWhenNoPersonalApiKey()
    {
        var container = new TestContainer(sp =>
        {
            var builder = new PostHogConfigurationBuilder(sp);
            builder.UseFeatureManagement<FakePostHogFeatureFlagContextProvider>();
            // No PersonalApiKey configured.
        });
        container.FakeHttpMessageHandler.AddFlagsResponse(
            requestPredicate: body =>
                body.TryGetValue("distinct_id", out var id)
                && id is System.Text.Json.JsonElement el
                && el.GetString() == FeatureEnumerationFallback.SentinelDistinctId,
            responseBody: """
            {
              "featureFlags": {
                "beta-feature": true,
                "alpha-feature": false
              }
            }
            """
        );
        var provider = container.Activate<PostHogFeatureDefinitionProvider>();

        var features = await provider.GetAllFeatureDefinitionsAsync().ToListAsync();

        Assert.Equal(
            new[] { "beta-feature", "alpha-feature" },
            features.Select(f => f.Name).OrderByDescending(n => n));
    }
}

public class TheGetFeatureDefinitionAsyncMethod
{
    [Fact]
    public async Task ReturnsSpecifiedFeature()
    {
        var container = new TestContainer(sp =>
        {
            var builder = new PostHogConfigurationBuilder(sp);
            builder.UseFeatureManagement<FakePostHogFeatureFlagContextProvider>();
            builder.PostConfigure(o => o.SecretKey = "fake-personal-api-key");
        });
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
            "flags":[
              {
                 "id":1,
                 "name":"Beta Feature",
                 "key":"beta-feature",
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
                 "name":"Alpha Feature",
                 "key":"alpha-feature",
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
        var provider = container.Activate<PostHogFeatureDefinitionProvider>();

        var feature = await provider.GetFeatureDefinitionAsync("beta-feature");

        Assert.NotNull(feature);
        Assert.Equal("beta-feature", feature.Name);
        var enabledFor = Assert.Single(feature.EnabledFor);
        Assert.Equal("PostHog", enabledFor.Name);
        Assert.Equal(RequirementType.Any, feature.RequirementType);
    }
}