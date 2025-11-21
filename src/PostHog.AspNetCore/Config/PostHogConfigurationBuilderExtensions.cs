using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;
using PostHog.Cache;
using PostHog.FeatureManagement;
using PostHog.Library;

namespace PostHog.Config;

using static Ensure;

/// <summary>
/// Extension methods for <see cref="IPostHogConfigurationBuilder"/>.
/// </summary>
public static class PostHogConfigurationBuilderExtensions
{
    /// <summary>
    /// Registers ASP.NET Core specific implementations of PostHogClient services.
    /// </summary>
    /// <param name="builder">The <see cref="IPostHogConfigurationBuilder"/>.</param>
    /// <returns>The passed in <see cref="IPostHogConfigurationBuilder"/>.</returns>
    public static IPostHogConfigurationBuilder UseAspNetCore(this IPostHogConfigurationBuilder builder)
    {
        NotNull(builder).Use(services =>
        {
            services.AddSingleton<IFeatureFlagCache, HttpContextFeatureFlagCache>();
            services.AddHttpContextAccessor();
        });
        return builder;
    }

    /// <summary>
    /// Adds the FeatureManagement services to the <see cref="IServiceCollection"/>. This allows using PostHog
    /// feature management as a provider for Microsoft.FeatureManagement.
    /// </summary>
    /// <remarks>
    /// This can be further configured with a custom <see cref="IPostHogFeatureFlagContextProvider"/> used to supply
    /// person properties and groups when evaluating feature flags. There's a base implementation of
    /// <see cref="PostHogFeatureFlagContextProvider"/> you can override.
    /// </remarks>
    /// <param name="builder">The <see cref="IPostHogConfigurationBuilder"/>.</param>
    /// <returns>The passed in <see cref="IPostHogConfigurationBuilder"/>.</returns>
    public static IPostHogConfigurationBuilder UseFeatureManagement<TContextProvider>(this IPostHogConfigurationBuilder builder)
        where TContextProvider : class, IPostHogFeatureFlagContextProvider =>
        builder.UseFeatureManagement<TContextProvider>(null);

    /// <summary>
    /// Adds the FeatureManagement services to the <see cref="IServiceCollection"/>. This allows using PostHog
    /// feature management as a provider for Microsoft.FeatureManagement.
    /// </summary>
    /// <remarks>
    /// This can be further configured with a custom <see cref="IPostHogFeatureFlagContextProvider"/> used to supply
    /// person properties and groups when evaluating feature flags. There's a base implementation of
    /// <see cref="PostHogFeatureFlagContextProvider"/> you can override.
    /// </remarks>
    /// <param name="builder">The <see cref="IPostHogConfigurationBuilder"/>.</param>
    /// <param name="configure">
    /// Action used to configure the <see cref="IFeatureManagementBuilder"/>. Use this to provide a custom
    /// <see cref="ITargetingContextAccessor"/>.
    /// </param>
    /// <returns>The passed in <see cref="IPostHogConfigurationBuilder"/>.</returns>
    public static IPostHogConfigurationBuilder UseFeatureManagement<TContextProvider>(
        this IPostHogConfigurationBuilder builder,
        Action<IPostHogFeatureManagementBuilder>? configure)
        where TContextProvider : class, IPostHogFeatureFlagContextProvider =>
        NotNull(builder).Use(services =>
        {
            services.AddService<IFeatureDefinitionProvider, PostHogFeatureDefinitionProvider>();
            services.AddService<IVariantFeatureManager, PostHogVariantFeatureManager>();
            var featureManagementBuilder = services.AddFeatureManagement();
            var postHogFeatureManagementBuilder = new PostHogFeatureManagementBuilder(featureManagementBuilder);
            featureManagementBuilder.ConfigureTargeting<TContextProvider>();
            configure?.Invoke(postHogFeatureManagementBuilder);
            featureManagementBuilder.AddFeatureFilter<PostHogFeatureFilter>();
        });

    static void ConfigureTargeting<TContextProvider>(this IFeatureManagementBuilder builder)
        where TContextProvider : class, IPostHogFeatureFlagContextProvider
    {
        // Register the public IPostHogFeatureFlagContextProvider implementation we expect consumers to implement.
        builder.Services.AddService<IPostHogFeatureFlagContextProvider, TContextProvider>();

        // Register our internal ITargetingContextAccessor implementation
        builder.WithTargeting<PostHogTargetingContextAccessor>();
    }

    static void AddService<TInterface, TImplementation>(this IServiceCollection services)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        if (services.IsFeatureManagerScoped())
        {
            services.AddScoped<TInterface, TImplementation>();
        }
        else
        {
            services.AddSingleton<TInterface, TImplementation>();
        }
    }

    static bool IsFeatureManagerScoped(this IServiceCollection services)
        => services.Any(descriptor => descriptor.ServiceType == typeof(IFeatureManager)
                                      && descriptor.Lifetime == ServiceLifetime.Scoped);
}