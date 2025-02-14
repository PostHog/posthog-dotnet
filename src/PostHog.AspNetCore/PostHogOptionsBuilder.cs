using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PostHog.Cache;
using PostHog.Library;

namespace PostHog;

/// <summary>
/// A builder for configuring PostHog options.
/// </summary>
/// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
public class PostHogOptionsBuilder(IHostApplicationBuilder builder)
{
    const string DefaultConfigurationSectionName = "PostHog";

    IConfigurationSection? _configurationSection;
    Action<IHttpClientBuilder>? _httpClientBuilder;

    /// <summary>
    /// Specifies the name of the configuration section to use for PostHog options. If this call is omitted,
    /// "PostHog" is used by default.
    /// </summary>
    /// <param name="configurationSectionName">The name of the configuration section that contains the PostHog settings.</param>
    /// <returns>The <see cref="PostHogOptionsBuilder"/>.</returns>
    public PostHogOptionsBuilder UseConfigurationSection(string configurationSectionName)
        => UseConfigurationSection(builder.Configuration.GetSection(configurationSectionName));

    /// <summary>
    /// Specifies the the configuration section to use for PostHog options. If this call is omitted,
    /// "PostHog" is used by default.
    /// </summary>
    /// <param name="configurationSection">The <see cref="IConfigurationSection"/> that contains the PostHog settings.</param>
    /// <returns>The <see cref="PostHogOptionsBuilder"/>.</returns>
    public PostHogOptionsBuilder UseConfigurationSection(IConfigurationSection configurationSection)
    {
        _configurationSection = configurationSection;
        return this;
    }

    /// <summary>
    /// Can be used to configure the <see cref="HttpClient"/> used by the PostHog client. For example, to add
    /// other handlers or change the timeout.
    /// </summary>
    /// <param name="httpClientBuilder">The <see cref="IHttpClientBuilder"/> used to build the <see cref="HttpClient"/>.</param>
    /// <returns>The <see cref="PostHogOptionsBuilder"/>.</returns>
    public PostHogOptionsBuilder ConfigureHttpClient(Action<IHttpClientBuilder> httpClientBuilder)
    {
        _httpClientBuilder = httpClientBuilder;
        return this;
    }

    internal void Build()
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var configurationSection = _configurationSection ?? configuration.GetSection(DefaultConfigurationSectionName);
        services.Configure<PostHogOptions>(configurationSection);

        services.AddSingleton<IFeatureFlagCache, HttpContextFeatureFlagCache>();
        services.AddSingleton<ITaskScheduler, TaskRunTaskScheduler>();
        services.AddSingleton<IPostHogClient, PostHogClient>();
        services.AddHttpContextAccessor();
        _httpClientBuilder?.Invoke(services.AddHttpClient(nameof(PostHogClient)));
    }
}
