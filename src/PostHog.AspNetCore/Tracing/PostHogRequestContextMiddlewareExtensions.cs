using Microsoft.AspNetCore.Builder;

namespace PostHog;

/// <summary>
/// Extension methods for adding PostHog request context support to an ASP.NET Core pipeline.
/// </summary>
public static class PostHogRequestContextMiddlewareExtensions
{
    /// <summary>
    /// Adds request-scoped PostHog context extraction for <c>X-POSTHOG-*</c> headers.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configure">Optional configuration for exception capture and privacy-sensitive request metadata.</param>
    /// <returns>The passed in <see cref="IApplicationBuilder" />.</returns>
    public static IApplicationBuilder UsePostHogRequestContext(
        this IApplicationBuilder app,
        Action<PostHogRequestContextOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = new PostHogRequestContextOptions();
        configure?.Invoke(options);
        return app.UseMiddleware<PostHogRequestContextMiddleware>(options);
    }
}
