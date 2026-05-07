using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PostHog;
using PostHog.Sdk;
using PostHog.Api;
using PostHog.Features;

namespace UnitTests;

[CollectionDefinition(nameof(PostHogSDKTestScope), DisableParallelization = true)]
public sealed class PostHogSDKTestScope
{
}

[Collection(nameof(PostHogSDKTestScope))]
public sealed class PostHogSDKTests : IDisposable
{
    public PostHogSDKTests()
    {
        PostHogSDK.DefaultClient = null;
        PostHogSDK.LoggerFactory = NullLoggerFactory.Instance;
    }

    [Fact]
    public void CaptureDelegatesToDefaultClient()
    {
        var client = Substitute.For<IPostHogClient>();
        client.Capture("user-123", "Test Event", null, null, (FeatureFlagEvaluations?)null, null).Returns(true);
        PostHogSDK.DefaultClient = client;

        var captured = PostHogSDK.Capture("user-123", "Test Event");

        Assert.True(captured);
        client.Received(1).Capture("user-123", "Test Event", null, null, (FeatureFlagEvaluations?)null, null);
    }

    [Fact]
    public async Task IdentifyAsyncDelegatesToDefaultClient()
    {
        var client = Substitute.For<IPostHogClient>();
        client.IdentifyAsync("user-123", null, null, CancellationToken.None)
            .Returns(Task.FromResult(new ApiResult(1)));
        PostHogSDK.DefaultClient = client;

        var result = await PostHogSDK.IdentifyAsync("user-123");

        Assert.Equal(1, result.Status);
        await client.Received(1).IdentifyAsync("user-123", null, null, CancellationToken.None);
    }

    [Fact]
    public void CaptureIsNoOpWithoutDefaultClient()
    {
        var captured = PostHogSDK.Capture("user-123", "Test Event");

        Assert.False(captured);
    }

    [Fact]
    public void NoDefaultClientWarningUsesConfiguredLoggerOnce()
    {
        var loggerFactory = new FakeLoggerProvider();
        PostHogSDK.LoggerFactory = loggerFactory;

        PostHogSDK.Capture("user-123", "Test Event");
        PostHogSDK.Capture("user-123", "Test Event");

        var warnings = loggerFactory.GetAllEvents(
            "PostHog.Sdk.PostHogSDK",
            LogLevel.Warning,
            "LogWarningNoDefaultClient");
        var warning = Assert.Single(warnings);
        Assert.Contains("PostHogSDK.DefaultClient is not configured", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(PostHogSDK.IdentifyAsync))]
    [InlineData(nameof(PostHogSDK.IsFeatureEnabledAsync))]
    [InlineData(nameof(PostHogSDK.GetFeatureFlagAsync))]
    public async Task AsyncCallIsNoOpWithoutDefaultClient(string method)
    {
        switch (method)
        {
            case nameof(PostHogSDK.IdentifyAsync):
                var identifyResult = await PostHogSDK.IdentifyAsync("user-123");
                Assert.Equal(0, identifyResult.Status);
                break;
            case nameof(PostHogSDK.IsFeatureEnabledAsync):
                var enabled = await PostHogSDK.IsFeatureEnabledAsync("beta-feature", "user-123");
                Assert.False(enabled);
                break;
            case nameof(PostHogSDK.GetFeatureFlagAsync):
                var flag = await PostHogSDK.GetFeatureFlagAsync("beta-feature", "user-123");
                Assert.Null(flag);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, null);
        }
    }

    [Fact]
    public void InitCreatesAndStoresDefaultClient()
    {
        var client = PostHogSDK.Init(new PostHogOptions { ProjectToken = "test-token" });

        Assert.Same(client, PostHogSDK.DefaultClient);
    }

    [Fact]
    public async Task ShutdownAsyncDisposesAndClearsDefaultClient()
    {
        var client = Substitute.For<IPostHogClient>();
        client.FlushAsync().Returns(Task.CompletedTask);
        PostHogSDK.DefaultClient = client;

        await PostHogSDK.ShutdownAsync();

        Assert.Null(PostHogSDK.DefaultClient);
        await client.Received(1).FlushAsync();
        await client.Received(1).DisposeAsync();
    }

    public void Dispose()
    {
        PostHogSDK.ShutdownAsync().AsTask().GetAwaiter().GetResult();
        PostHogSDK.LoggerFactory = NullLoggerFactory.Instance;
    }
}
