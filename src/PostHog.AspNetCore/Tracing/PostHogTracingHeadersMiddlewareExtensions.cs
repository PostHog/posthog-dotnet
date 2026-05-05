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
    /// <param name="captureExceptions">If <c>true</c>, unhandled downstream exceptions are captured and rethrown.</param>
    /// <returns>The passed in <see cref="IApplicationBuilder" />.</returns>
    public static IApplicationBuilder UsePostHogTracingHeaders(
        this IApplicationBuilder app,
        bool captureExceptions = true)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<PostHogTracingHeadersMiddleware>(captureExceptions);
    }
}
