using PostHog;
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
}
