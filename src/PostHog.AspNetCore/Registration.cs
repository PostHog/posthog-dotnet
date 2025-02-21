using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PostHog.Config;
using static PostHog.Library.Ensure;

namespace PostHog;

/// <summary>
/// Extension methods on <see cref="IHostApplicationBuilder"/> used to register
/// a <see cref="PostHogClient"/> configured for ASP.NET Core.
/// </summary>
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
        => builder.AddPostHog(null);

    /// <summary>
    /// Registers <see cref="PostHogClient"/> as a singleton.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <param name="options">Provides a mean to configure the <see cref="PostHogClient"/> and some of the services it uses.</param>
    /// <returns>The passed in <see cref="IHostApplicationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">If <see cref="builder"/> is null.</exception>
    public static IHostApplicationBuilder AddPostHog(
        this IHostApplicationBuilder builder,
        Action<IPostHogConfigurationBuilder>? options)
    {
        builder = NotNull(builder);

        builder.Services.AddPostHog(
            o =>
            {
                if (builder.Services.All(service => service.ServiceType != typeof(IConfigureOptions<PostHogOptions>)))
                {
                    // Set the default.
                    o.UseConfigurationSection(builder.Configuration.GetSection(DefaultConfigurationSectionName));
                }
                o.UseAspNetCore();
                options?.Invoke(o);
            });
        return builder;
    }
}