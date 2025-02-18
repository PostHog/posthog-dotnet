using PostHog.Config;
using PostHog.FeatureManagement;
using UnitTests.Fakes;
using UnitTests.Library;

namespace PostHogVariantFeatureManagerTests;

public class TheGetFeatureNamesAsyncMethod
{
    [Fact]
    public async Task ReturnsAllActiveFeatures()
    {
        var container = new TestContainer(sp =>
        {
            var builder = new PostHogConfigurationBuilder(sp);
            builder.UseFeatureManagement<FakePostHogFeatureFlagContextProvider>();
            builder.PostConfigure(o => o.PersonalApiKey = "fake-personal-api-key");
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
        var featureManager = container.Activate<PostHogVariantFeatureManager>();

        var featureNames = await featureManager.GetFeatureNamesAsync().ToListAsync();

        Assert.Equal(["beta-feature", "alpha-feature"], featureNames);
    }
}

public class TheIsEnabledAsyncMethod
{
    [Fact]
    public async Task ReturnsTrueForEnabledFlag()
    {
        var container = new TestContainer(sp =>
        {
            var builder = new PostHogConfigurationBuilder(sp);
            builder.UseFeatureManagement<FakePostHogFeatureFlagContextProvider>();
            builder.PostConfigure(o => o.PersonalApiKey = "fake-personal-api-key");
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
        var featureManager = container.Activate<PostHogVariantFeatureManager>();

        Assert.True(await featureManager.IsEnabledAsync("beta-feature"));
        Assert.False(await featureManager.IsEnabledAsync("alpha-feature"));
    }
}

public class TheGetVariantAsyncMethod
{
    [Fact]
    public async Task ReturnsVariantForEnabledFlag()
    {
        var container = new TestContainer(sp =>
        {
            var builder = new PostHogConfigurationBuilder(sp);
            builder.UseFeatureManagement<FakePostHogFeatureFlagContextProvider>();
            builder.PostConfigure(o => o.PersonalApiKey = "fake-personal-api-key");
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
        var featureManager = container.Activate<PostHogVariantFeatureManager>();

        var context = new PostHogFeatureFlagContext
        {
            DistinctId = "test_id",
            PersonProperties = new() { ["email"] = "test@posthog.com" }
        };
        var targetingContext = new PostHogTargetingContext(context);
        Assert.Equal(
            "second-variant",
            (await featureManager.GetVariantAsync("beta-feature", targetingContext)).Name);
        context = new PostHogFeatureFlagContext
        {
            DistinctId = "example_id",
        };
        targetingContext = new PostHogTargetingContext(context);
        Assert.Equal(
            "third-variant",
            (await featureManager.GetVariantAsync("beta-feature", targetingContext)).Name);
        context = new PostHogFeatureFlagContext
        {
            DistinctId = "another_id",
        };
        targetingContext = new PostHogTargetingContext(context);
        Assert.Equal(
            "second-variant",
            (await featureManager.GetVariantAsync("beta-feature", targetingContext)).Name);
    }
}
public class FakePostHogFeatureFlagContextProvider : PostHogFeatureFlagContextProvider
{
    protected override string GetDistinctId() => "fake-distinct-id";
}
