using Microsoft.AspNetCore.Builder;

namespace PostHog;

/// <summary>
/// Extension methods for adding PostHog tracing header support to an ASP.NET Core pipeline.
/// </summary>
public static class PostHogTracingHeadersMiddlewareExtensions
{
    /// <summary>
    /// Adds request-scoped PostHog context extraction for <c>X-POSTHOG-*</c> tracing headers.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configure">Optional configuration for exception capture and privacy-sensitive request metadata.</param>
    /// <returns>The passed in <see cref="IApplicationBuilder" />.</returns>
    public static IApplicationBuilder UsePostHogTracingHeaders(
        this IApplicationBuilder app,
        Action<PostHogTracingHeadersOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = new PostHogTracingHeadersOptions();
        configure?.Invoke(options);
        return app.UseMiddleware<PostHogTracingHeadersMiddleware>(options);
    }
}
