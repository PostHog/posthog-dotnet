using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace PostHog.AI.Tests;

public sealed class PostHogAIExtensionsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AddPostHogOpenAIClientThrowsOnNullOrWhitespaceApiKey(string? apiKey)
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(
            () => services.AddPostHogOpenAIClient(apiKey!)
        );
    }

    [Fact]
    public void AddPostHogOpenAIClientThrowsWhenPostHogNotRegistered()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(
            () => services.AddPostHogOpenAIClient("sk-test-key")
        );
    }

    [Fact]
    public void AddPostHogOpenAIClientSucceedsWhenPostHogIsRegistered()
    {
        var services = new ServiceCollection();
        // Register a substitute IPostHogClient
        services.AddSingleton(
            Substitute.For<IPostHogClient>()
        );

        // Should not throw
        var builder = services.AddPostHogOpenAIClient("sk-test-key");
        Assert.NotNull(builder);
    }
}
