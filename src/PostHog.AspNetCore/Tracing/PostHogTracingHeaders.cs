using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using PostHog.Api;

namespace PostHog;

/// <summary>
/// Helpers for extracting PostHog tracing context from ASP.NET Core requests.
/// </summary>
internal static class PostHogTracingHeaders
{
    /// <summary>
    /// The frontend distinct ID tracing header.
    /// </summary>
    public const string DistinctId = "X-POSTHOG-DISTINCT-ID";

    /// <summary>
    /// The frontend session ID tracing header.
    /// </summary>
    public const string SessionId = "X-POSTHOG-SESSION-ID";

    /// <summary>
    /// The frontend window ID tracing header used by Node SDK integrations.
    /// </summary>
    public const string WindowId = "X-POSTHOG-WINDOW-ID";

    const int MaxHeaderValueLength = 1000;

    /// <summary>
    /// Extracts sanitized tracing identity and request metadata from an ASP.NET Core HTTP context.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="options">Options controlling privacy-sensitive request metadata extraction.</param>
    /// <returns>The extracted tracing context.</returns>
    public static PostHogTracingContext Extract(HttpContext httpContext, PostHogTracingHeadersOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(options);

        var request = httpContext.Request;
        var distinctId = SanitizeHeaderValue(request.Headers[DistinctId]);
        if (string.IsNullOrEmpty(distinctId) && options.UseAuthenticatedUserIdWhenDistinctIdHeaderMissing)
        {
            distinctId = GetAuthenticatedUserId(httpContext.User);
        }

        var sessionId = SanitizeHeaderValue(request.Headers[SessionId]);
        var windowId = SanitizeHeaderValue(request.Headers[WindowId]);

        var properties = new Dictionary<string, object>();
        AddIfPresent(properties, PostHogProperties.CurrentUrl, GetCurrentUrl(request, options.IncludeQueryStringInCurrentUrl));
        AddIfPresent(properties, PostHogProperties.RequestMethod, request.Method);
        AddIfPresent(properties, PostHogProperties.RequestPath, GetRequestPath(request));
        AddIfPresent(properties, PostHogProperties.UserAgent, SanitizeHeaderValue(request.Headers[HeaderNames.UserAgent]));
        if (options.CaptureClientIp)
        {
            AddIfPresent(properties, PostHogProperties.Ip, httpContext.Connection.RemoteIpAddress?.ToString());
        }
        AddIfPresent(properties, PostHogProperties.WindowId, windowId);

        if (!string.IsNullOrEmpty(sessionId))
        {
            properties[PostHogProperties.SessionId] = sessionId;
        }

        return new PostHogTracingContext(distinctId, sessionId, properties);
    }

    internal static string? SanitizeHeaderValue(StringValues values)
    {
        if (StringValues.IsNullOrEmpty(values))
        {
            return null;
        }

        var value = values.Count > 0 ? values[0] : values.ToString();
        return SanitizeValue(value);
    }

    static string? SanitizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (sanitized.Length == 0)
        {
            return null;
        }

        return sanitized.Length <= MaxHeaderValueLength ? sanitized : sanitized[..MaxHeaderValueLength];
    }

    static void AddIfPresent(Dictionary<string, object> properties, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            properties[key] = value;
        }
    }

    static string? GetAuthenticatedUserId(ClaimsPrincipal user)
    {
        var identity = user.Identity;
        if (identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? identity.Name;
        return SanitizeValue(userId);
    }

    static string? GetCurrentUrl(HttpRequest request, bool includeQueryString)
    {
        if (string.IsNullOrEmpty(request.Scheme) || !request.Host.HasValue)
        {
            return null;
        }

        var url = string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent(),
            request.Path.ToUriComponent());

        return includeQueryString
            ? string.Concat(url, request.QueryString.ToUriComponent())
            : url;
    }

    static string? GetRequestPath(HttpRequest request)
    {
        var path = string.Concat(request.PathBase.ToUriComponent(), request.Path.ToUriComponent());
        return string.IsNullOrEmpty(path) ? null : path;
    }
}

/// <summary>
/// Sanitized PostHog tracing context extracted from an ASP.NET Core request.
/// </summary>
/// <param name="DistinctId">The analytics distinct ID to apply to captures without an explicit distinct ID.</param>
/// <param name="SessionId">The session ID to apply as <c>$session_id</c> when captures do not provide one explicitly.</param>
/// <param name="Properties">Request metadata to add to captures before explicit capture properties.</param>
internal sealed record PostHogTracingContext(
    string? DistinctId,
    string? SessionId,
    IReadOnlyDictionary<string, object> Properties);
