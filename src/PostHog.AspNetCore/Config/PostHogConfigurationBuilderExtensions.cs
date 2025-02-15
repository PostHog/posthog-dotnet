using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PostHog.Cache;
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
}