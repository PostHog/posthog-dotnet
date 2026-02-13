using Microsoft.Extensions.DependencyInjection;

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
        // Register a mock IPostHogClient
        services.AddSingleton<IPostHogClient>(
            new Moq.Mock<IPostHogClient>().Object
        );

        // Should not throw
        var builder = services.AddPostHogOpenAIClient("sk-test-key");
        Assert.NotNull(builder);
    }
}
