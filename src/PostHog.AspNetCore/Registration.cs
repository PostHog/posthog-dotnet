using Microsoft.Extensions.Hosting;
using static PostHog.Library.Ensure;

namespace PostHog;

public static class Registration
{
    /// <summary>
    /// Registers <see cref="PostHogClient"/> as a singleton. Looks for client configuration in the "PostHog"
    /// section of the configuration. See <see cref="PostHogOptions"/> for the configuration options.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <returns>The passed in <see cref="IHostApplicationBuilder"/>.</returns>
    public static IHostApplicationBuilder AddPostHog(this IHostApplicationBuilder builder)
        => builder.AddPostHog(_ => { });

    /// <summary>
    /// Registers <see cref="PostHogClient"/> as a singleton.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <param name="options">Provides a mean to configure the <see cref="PostHogClient"/> and some of the services it uses.</param>
    /// <returns>The passed in <see cref="IHostApplicationBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException">If <see cref="builder"/> is null.</exception>
    public static IHostApplicationBuilder AddPostHog(
        this IHostApplicationBuilder builder,
        Action<PostHogOptionsBuilder> options)
    {
        options = NotNull(options);
        var postHogOptionsBuilder = new PostHogOptionsBuilder(builder);
        options(postHogOptionsBuilder);
        postHogOptionsBuilder.Build();
        return builder;
    }
}