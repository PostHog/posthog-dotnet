using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PostHog.Library;
using static PostHog.Library.Ensure;

namespace PostHog.Config;

/// <summary>
/// A builder for configuring the <see cref="PostHogClient"/>.
/// </summary>
public class PostHogConfigurationBuilder : IPostHogConfigurationBuilder
{
    readonly IServiceCollection _services;
    readonly IHttpClientBuilder _httpClientBuilder;

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
    /// <returns></returns>
    public IServiceCollection Build()
    {
        // Try to set up defaults if they haven't been set yet.
        _services.TryAddSingleton<IFeatureFlagCache>(_ => NullFeatureFlagCache.Instance);
        _services.TryAddSingleton<ITaskScheduler, TaskRunTaskScheduler>();
        _services.TryAddSingleton<IPostHogClient, PostHogClient>();
        _services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);

        return _services;
    }

    /// <inheritdoc />
    public IPostHogConfigurationBuilder Use(Action<IServiceCollection>? configurationAction)
    {
        configurationAction?.Invoke(_services);
        return this;
    }
}