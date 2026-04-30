using NSubstitute;
using PostHog;
using PostHog.Api;

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
        client.Capture("user-123", "Test Event", null, null, false, null).Returns(true);
        PostHogSdk.DefaultClient = client;

        var captured = PostHogSdk.Capture("user-123", "Test Event");

        Assert.True(captured);
        client.Received(1).Capture("user-123", "Test Event", null, null, false, null);
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

    [Fact]
    public async Task AsyncCallsAreNoOpWithoutDefaultClient()
    {
        var identifyResult = await PostHogSdk.IdentifyAsync("user-123");
        var enabled = await PostHogSdk.IsFeatureEnabledAsync("beta-feature", "user-123");
        var flag = await PostHogSdk.GetFeatureFlagAsync("beta-feature", "user-123");

        Assert.Equal(0, identifyResult.Status);
        Assert.False(enabled);
        Assert.Null(flag);
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
