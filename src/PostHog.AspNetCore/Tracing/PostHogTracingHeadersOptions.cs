namespace PostHog;

/// <summary>
/// Options for <see cref="PostHogTracingHeadersMiddlewareExtensions.UsePostHogTracingHeaders" />.
/// </summary>
public sealed class PostHogTracingHeadersOptions
{
    /// <summary>
    /// Captures unhandled downstream exceptions with the active request context, then rethrows them.
    /// Defaults to <c>true</c>, matching PostHog server-side context helpers in other SDKs.
    /// </summary>
    public bool CaptureExceptions { get; set; } = true;

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
    /// Defaults to <c>true</c>. Disable this if your server-side user ID does not match the
    /// distinct ID used by your frontend PostHog SDK.
    /// </summary>
    public bool UseAuthenticatedUserIdWhenDistinctIdHeaderMissing { get; set; } = true;
}
