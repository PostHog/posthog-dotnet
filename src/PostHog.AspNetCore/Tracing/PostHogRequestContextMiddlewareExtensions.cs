using Microsoft.AspNetCore.Builder;

namespace PostHog;

/// <summary>
/// Extension methods for adding PostHog request context support to an ASP.NET Core pipeline.
/// </summary>
public static class PostHogRequestContextMiddlewareExtensions
{
    /// <summary>
    /// Adds request-scoped PostHog context extraction for PostHog tracing headers and request metadata.
    /// </summary>
    /// <remarks>
    /// Request metadata includes <c>$ip</c> from <c>HttpContext.Connection.RemoteIpAddress</c>. In proxied deployments,
    /// call ASP.NET Core forwarded-headers middleware before this middleware; otherwise PostHog may receive an internal
    /// proxy, load-balancer, or pod IP.
    /// </remarks>
    /// <param name="app">The application builder.</param>
    /// <param name="configure">Optional configuration for tracing headers and exception capture.</param>
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
