using NSubstitute;
using PostHog;
using PostHog.Api;
using PostHog.Features;

namespace UnitTests;

[CollectionDefinition(nameof(PostHogSdkTestScope), DisableParallelization = true)]
public sealed class PostHogSdkTestScope
{
}

[Collection(nameof(PostHogSdkTestScope))]
public sealed class PostHogSdkTests : IDisposable
{
    public PostHogSdkTests()
    {
        PostHogSdk.DefaultClient = null;
    }

    [Fact]
    public void CaptureDelegatesToDefaultClient()
    {
        var client = Substitute.For<IPostHogClient>();
        client.Capture("user-123", "Test Event", null, null, (FeatureFlagEvaluations?)null, null).Returns(true);
        PostHogSdk.DefaultClient = client;

        var captured = PostHogSdk.Capture("user-123", "Test Event");

        Assert.True(captured);
        client.Received(1).Capture("user-123", "Test Event", null, null, (FeatureFlagEvaluations?)null, null);
    }

    [Fact]
    public async Task IdentifyAsyncDelegatesToDefaultClient()
    {
        var client = Substitute.For<IPostHogClient>();
        client.IdentifyAsync("user-123", null, null, CancellationToken.None)
            .Returns(Task.FromResult(new ApiResult(1)));
        PostHogSdk.DefaultClient = client;

        var result = await PostHogSdk.IdentifyAsync("user-123");

        Assert.Equal(1, result.Status);
        await client.Received(1).IdentifyAsync("user-123", null, null, CancellationToken.None);
    }

    [Fact]
    public void CaptureIsNoOpWithoutDefaultClient()
    {
        var captured = PostHogSdk.Capture("user-123", "Test Event");

        Assert.False(captured);
    }

    [Theory]
    [InlineData(nameof(PostHogSdk.IdentifyAsync))]
    [InlineData(nameof(PostHogSdk.IsFeatureEnabledAsync))]
    [InlineData(nameof(PostHogSdk.GetFeatureFlagAsync))]
    public async Task AsyncCallIsNoOpWithoutDefaultClient(string method)
    {
        switch (method)
        {
            case nameof(PostHogSdk.IdentifyAsync):
                var identifyResult = await PostHogSdk.IdentifyAsync("user-123");
                Assert.Equal(0, identifyResult.Status);
                break;
            case nameof(PostHogSdk.IsFeatureEnabledAsync):
                var enabled = await PostHogSdk.IsFeatureEnabledAsync("beta-feature", "user-123");
                Assert.False(enabled);
                break;
            case nameof(PostHogSdk.GetFeatureFlagAsync):
                var flag = await PostHogSdk.GetFeatureFlagAsync("beta-feature", "user-123");
                Assert.Null(flag);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, null);
        }
    }

    [Fact]
    public void InitCreatesAndStoresDefaultClient()
    {
        var client = PostHogSdk.Init(new PostHogOptions { ProjectToken = "test-token" });

        Assert.Same(client, PostHogSdk.DefaultClient);
    }

    [Fact]
    public async Task ShutdownAsyncDisposesAndClearsDefaultClient()
    {
        var client = Substitute.For<IPostHogClient>();
        client.FlushAsync().Returns(Task.CompletedTask);
        PostHogSdk.DefaultClient = client;

        await PostHogSdk.ShutdownAsync();

        Assert.Null(PostHogSdk.DefaultClient);
        await client.Received(1).FlushAsync();
        await client.Received(1).DisposeAsync();
    }

    public void Dispose()
    {
        PostHogSdk.ShutdownAsync().AsTask().GetAwaiter().GetResult();
    }
}
