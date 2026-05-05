using Microsoft.AspNetCore.Http;

namespace PostHog;

/// <summary>
/// Options for <see cref="PostHogRequestContextMiddlewareExtensions.UsePostHogRequestContext" />.
/// </summary>
public sealed class PostHogRequestContextOptions
{
    /// <summary>
    /// Captures unhandled downstream exceptions with the active request context, then rethrows them.
    /// Defaults to <c>false</c> to avoid duplicate exception capture in applications that already
    /// handle exceptions elsewhere.
    /// </summary>
    public bool CaptureExceptions { get; set; }

    /// <summary>
    /// Minimum response status code required for unhandled exception capture. Responses below this
    /// threshold are rethrown without capture. Defaults to <c>500</c>.
    /// </summary>
    public int MinimumExceptionStatusCode { get; set; } = StatusCodes.Status500InternalServerError;

    /// <summary>
    /// Includes the request query string in <c>$current_url</c>. Defaults to <c>false</c>
    /// because query strings frequently contain tokens or other sensitive data.
    /// </summary>
    public bool IncludeQueryStringInCurrentUrl { get; set; }

    /// <summary>
    /// Adds the ASP.NET Core client IP address as <c>$ip</c>. Defaults to <c>false</c>
    /// because IP addresses are personal data. If your app is behind a proxy, configure
    /// ASP.NET Core forwarded headers before this middleware and this option will use
    /// <c>HttpContext.Connection.RemoteIpAddress</c> after that normalization.
    /// </summary>
    public bool CaptureClientIp { get; set; }

    /// <summary>
    /// Uses the authenticated ASP.NET Core user ID when <c>X-POSTHOG-DISTINCT-ID</c> is missing.
    /// Defaults to <c>false</c> because backend user IDs often differ from frontend PostHog distinct IDs.
    /// Enable only when those identifiers intentionally match.
    /// </summary>
    public bool UseAuthenticatedUserIdWhenDistinctIdHeaderMissing { get; set; }
}
