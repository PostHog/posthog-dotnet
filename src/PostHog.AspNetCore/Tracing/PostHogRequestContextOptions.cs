namespace PostHog;

/// <summary>
/// Options for <see cref="PostHogRequestContextMiddlewareExtensions.UsePostHogRequestContext" />.
/// </summary>
/// <remarks>
/// Installing the request-context middleware opts the ASP.NET Core pipeline into request context extraction. Individual
/// PostHog calls use request-context identity only when they omit an explicit distinct ID or call a parameterless
/// request-context helper. Explicit distinct IDs always override request context.
/// </remarks>
public sealed class PostHogRequestContextOptions
{
    /// <summary>
    /// Uses client-supplied <c>X-PostHog-Distinct-Id</c> and <c>X-PostHog-Session-Id</c>
    /// headers as request-scoped analytics identity/session context. Defaults to <c>true</c>.
    /// When disabled, the middleware still creates request context and adds request metadata.
    /// </summary>
    /// <remarks>
    /// This controls whether the opted-in middleware can populate analytics identity/session from tracing headers.
    /// A call site still opts into that identity by omitting its distinct ID or using a parameterless request-context
    /// helper; calls with explicit distinct IDs use the explicit value.
    /// </remarks>
    public bool UseTracingHeaders { get; set; } = true;

    /// <summary>
    /// Includes the request query string in the <c>$current_url</c> request metadata property. Defaults to
    /// <c>false</c> to avoid sending server-side secrets such as OAuth codes, reset tokens, or signed URL parameters.
    /// </summary>
    public bool IncludeQueryStringInCurrentUrl { get; set; }

    /// <summary>
    /// Captures unhandled downstream exceptions with the active request context, then rethrows them.
    /// Defaults to <c>false</c> to avoid duplicate exception capture in applications that already
    /// handle exceptions elsewhere.
    /// </summary>
    public bool CaptureExceptions { get; set; }
}
