namespace PostHog;

/// <summary>
/// Options for <see cref="PostHogRequestContextMiddlewareExtensions.UsePostHogRequestContext" />.
/// </summary>
public sealed class PostHogRequestContextOptions
{
    /// <summary>
    /// Uses client-supplied <c>X-PostHog-Distinct-Id</c> and <c>X-PostHog-Session-Id</c>
    /// headers as request-scoped analytics identity/session context. Defaults to <c>true</c>.
    /// When disabled, the middleware still creates request context and adds request metadata.
    /// </summary>
    public bool UseTracingHeaders { get; set; } = true;

    /// <summary>
    /// Captures unhandled downstream exceptions with the active request context, then rethrows them.
    /// Defaults to <c>false</c> to avoid duplicate exception capture in applications that already
    /// handle exceptions elsewhere.
    /// </summary>
    public bool CaptureExceptions { get; set; }
}
