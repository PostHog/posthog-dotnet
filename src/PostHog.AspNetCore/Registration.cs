﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PostHog.Api;
using PostHog.Cache;
using PostHog.Config;

namespace PostHog;

public static class Registration
{
    const string DefaultConfigurationSectionName = "PostHog";

    /// <summary>
    /// Registers <see cref="PostHogClient"/> as a singleton. Looks for client configuration in the "PostHog"
    /// section of the configuration. See <see cref="PostHogOptions"/> for the configuration options.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <returns>The passed in <see cref="IHostApplicationBuilder"/>.</returns>
    public static IHostApplicationBuilder AddPostHog(this IHostApplicationBuilder builder)
        => builder.AddPostHog(DefaultConfigurationSectionName);

    /// <summary>
    /// Registers <see cref="PostHogClient"/> as a singleton. Looks for client configuration in the "PostHog"
    /// section of the configuration.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <param name="configurationSectionName">The configuration section name to grab PostHog options from.</param>
    /// <returns>The passed in <see cref="IHostApplicationBuilder"/>.</returns>
    public static IHostApplicationBuilder AddPostHog(this IHostApplicationBuilder builder, string configurationSectionName)
        => builder.AddPostHog(
            (builder ?? throw new ArgumentNullException(nameof(builder))).Configuration.GetSection(configurationSectionName));

    /// <summary>
    /// Registers <see cref="PostHogClient"/> as a singleton. Looks for client configuration in the supplied
    /// <paramref name="configurationSection"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <param name="configurationSection">The configuration section where to load config settings.</param>
    /// <returns>The passed in <see cref="IHostApplicationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">If <see cref="builder"/> is null.</exception>
    public static IHostApplicationBuilder AddPostHog(
        this IHostApplicationBuilder builder,
        IConfigurationSection configurationSection)
    {
        builder = builder ?? throw new ArgumentNullException(nameof(builder));

        builder.Services.Configure<PostHogOptions>(configurationSection);
        builder.Services.AddSingleton<IPostHogApiClient, PostHogApiClient>();
        builder.Services.AddSingleton<IFeatureFlagCache, HttpContextFeatureFlagCache>();
        builder.Services.AddSingleton<IPostHogClient, PostHogClient>();

        return builder;
    }
}