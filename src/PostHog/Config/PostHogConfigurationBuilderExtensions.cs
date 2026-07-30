using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static PostHog.Library.Ensure;

namespace PostHog.Config;

/// <summary>
/// Extension methods for <see cref="IPostHogConfigurationBuilder"/>.
/// </summary>
public static class PostHogConfigurationBuilderExtensions
{
    /// <summary>
    /// Allows overriding configured options. If using a configuration section, this needs to be called after
    /// <see cref="UseConfigurationSection"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IPostHogConfigurationBuilder"/>.</param>
    /// <param name="options">An action used to set configuration options.</param>
    /// <returns>The <see cref="IPostHogConfigurationBuilder"/>.</returns>
    public static IPostHogConfigurationBuilder PostConfigure(
        this IPostHogConfigurationBuilder builder,
        Action<PostHogOptions> options) =>
        NotNull(builder).Use(services =>
            services.PostConfigure<PostHogOptions>(options.Invoke));

    /// <summary>
    /// Use the specified configuration section to provide the <see cref="PostHogOptions"/> to the PostHog client.
    /// </summary>
    /// <param name="builder">The <see cref="IPostHogConfigurationBuilder"/>.</param>
    /// <param name="section">The <see cref="IConfigurationSection"/> containing <see cref="PostHogOptions"/>.</param>
    /// <returns>The <see cref="IPostHogConfigurationBuilder"/>.</returns>
    public static IPostHogConfigurationBuilder UseConfigurationSection(
        this IPostHogConfigurationBuilder builder,
        IConfigurationSection section) =>
        NotNull(builder).Use(services =>
        {
#if !(NETSTANDARD2_0 || NETSTANDARD2_1)
            services.AddSingleton<IOptionsChangeTokenSource<PostHogOptions>>(
                new ConfigurationChangeTokenSource<PostHogOptions>(section));
#endif
            services.Configure<PostHogOptions>(options =>
            {
                var projectKeyState = options.GetProjectKeyState();
                section.Bind(options);

                // ConfigurationBinder writes alias getter values back when their keys are absent. Restore the raw
                // values for absent keys so this section changes only the project key values it contains.
                options.RestoreProjectKeyState(
                    projectKeyState,
                    restoreProjectToken: section["ProjectToken"] is null,
                    restoreProjectApiKey: section["ProjectApiKey"] is null);
            });
        });
}