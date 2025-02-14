using PostHog;
using PostHog.Features;
using UnitTests.Fakes;

#pragma warning disable CA2000
namespace FeatureFlagExtensionsTests;

public class TheIsFeatureEnabledAsyncMethod
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReturnsFlagResult(bool enabled)
    {
        var container = new TestContainer();
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddDecideResponse(
            $$"""
            {"featureFlags": {"flag-key": {{enabled.ToString().ToLowerInvariant()}} } }
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.IsFeatureEnabledAsync("flag-key",
            "distinctId", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(enabled, result.Value);
    }

    [Fact]
    public async Task ReturnsTrueWhenFlagReturnsString()
    {
        var container = new TestContainer();
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddDecideResponse(
            """
            {"featureFlags":{"flag-key": "premium-experience"}}
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.IsFeatureEnabledAsync("flag-key", "distinctId");

        Assert.True(result);
    }

    [Fact]
    public async Task ReturnsFalseWhenFlagDoesNotExist() // TODO: Is this the correct behavior?
    {
        var container = new TestContainer();
        var messageHandler = container.FakeHttpMessageHandler;
        messageHandler.AddDecideResponse(
            """
            {"featureFlags":{"flag-key": "premium-experience"}}
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.IsFeatureEnabledAsync("not-flag-key", "distinctId");

        Assert.False(result);
    }

    [Fact]
    public async Task ReturnsNullWhenNoApiResponse()
    {
        var container = new TestContainer();
        var client = container.Activate<PostHogClient>();

        var result = await client.IsFeatureEnabledAsync("not-flag-key", "distinctId");

        Assert.Null(result);
    }

    [Fact]
    public async Task ReturnsTrueOnLocallyEvaluatedProperty()
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
                  }
               ]
            }
            """
        );
        var posthog = container.Activate<PostHogClient>();

        Assert.True(
            await posthog.IsFeatureEnabledAsync(
                "person-flag",
                distinctId: "some-distinct-id",
                personProperties: new() { ["region"] = "USA" })
        );
        Assert.False(
            await posthog.GetFeatureFlagAsync(
                "person-flag",
                distinctId: "some-distinct-2",
                personProperties: new() { ["region"] = "Canada" })
        );

        var flags = await posthog.GetAllFeatureFlagsAsync(
        "some-user-id",
        options: new AllFeatureFlagsOptions
        {
            Groups =
            [
                new Group("project", "aaaa-bbbb-cccc")
        {
            ["$group_key"] = "aaaa-bbbb-cccc",
            ["size"] = "large"
        }
            ]
        });
    }
}
