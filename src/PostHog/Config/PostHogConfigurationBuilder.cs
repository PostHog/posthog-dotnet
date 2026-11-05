using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PostHog.Emails;
using PostHog.Library;
using PostHog.Prompts;
using static PostHog.Library.Ensure;

namespace PostHog.Config;

/// <summary>
/// A builder for configuring the <see cref="PostHogClient"/>.
/// </summary>
public class PostHogConfigurationBuilder : IPostHogConfigurationBuilder
{
    readonly IServiceCollection _services;
    readonly IHttpClientBuilder _httpClientBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostHogConfigurationBuilder"/> class.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> where PostHog services are registered.</param>
    public PostHogConfigurationBuilder(IServiceCollection services)
    {
        _services = services;
        _services.AddLogging();
        _httpClientBuilder = _services.AddHttpClient(nameof(PostHogClient)); // Registers the IHttpClientFactory.
    }

    /// <inheritdoc />
    public IPostHogConfigurationBuilder ConfigureHttpClient(Action<IHttpClientBuilder> configureHttpClient)
    {
        NotNull(configureHttpClient)(_httpClientBuilder);
        return this;
    }

    /// <summary>
    /// Builds the <see cref="IServiceCollection"/> with the configured services. This enables us to try and add
    /// default services after we've configured the <see cref="PostHogClient"/>.
    /// </summary>
    /// <returns>The configured <see cref="IServiceCollection"/>.</returns>
    public IServiceCollection Build()
    {
        // Try to set up defaults if they haven't been set yet.
        _services.TryAddSingleton<IFeatureFlagCache>(_ => NullFeatureFlagCache.Instance);
        _services.TryAddSingleton<ITaskScheduler, TaskRunTaskScheduler>();
        _services.TryAddSingleton<IPostHogClient, PostHogClient>();
        _services.TryAddSingleton<IPostHogEmails>(EmailsOf);
        _services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);
        _services.TryAddSingleton<IPostHogPrompts>(PromptsOf);

        return _services;
    }

    static IPostHogEmails EmailsOf(IServiceProvider services)
        => services.GetRequiredService<IPostHogClient>() as PostHogClient is { } client
            ? client.Emails
            : throw new InvalidOperationException(
                "IPostHogEmails needs PostHogClient registered as IPostHogClient.");

    static IPostHogPrompts PromptsOf(IServiceProvider services)
        => services.GetRequiredService<IPostHogClient>() is PostHogClient client
            ? client.Prompts
            : throw new InvalidOperationException("IPostHogPrompts needs PostHogClient registered as IPostHogClient.");

    /// <inheritdoc />
    public IPostHogConfigurationBuilder Use(Action<IServiceCollection>? configurationAction)
    {
        configurationAction?.Invoke(_services);
        return this;
    }
}