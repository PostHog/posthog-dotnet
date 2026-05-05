using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using PostHog.Api;

namespace PostHog;

/// <summary>
/// Helpers for extracting PostHog tracing context from ASP.NET Core requests.
/// </summary>
public static class PostHogTracingHeaders
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

    const string XForwardedFor = "X-Forwarded-For";
    const int MaxHeaderValueLength = 1000;

    /// <summary>
    /// Extracts sanitized tracing identity and request metadata from an ASP.NET Core HTTP context.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The extracted tracing context.</returns>
    public static PostHogTracingContext Extract(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var request = httpContext.Request;
        var distinctId = SanitizeHeaderValue(request.Headers[DistinctId]) ?? GetAuthenticatedUserId(httpContext.User);
        var sessionId = SanitizeHeaderValue(request.Headers[SessionId]);
        var windowId = SanitizeHeaderValue(request.Headers[WindowId]);

        var properties = new Dictionary<string, object>();
        AddIfPresent(properties, PostHogProperties.CurrentUrl, GetCurrentUrl(request));
        AddIfPresent(properties, PostHogProperties.RequestMethod, request.Method);
        AddIfPresent(properties, PostHogProperties.RequestPath, GetRequestPath(request));
        AddIfPresent(properties, PostHogProperties.UserAgent, SanitizeHeaderValue(request.Headers[HeaderNames.UserAgent]));
        AddIfPresent(properties, PostHogProperties.Ip, GetClientIp(httpContext));
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

        var value = values.ToString();
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
        return string.IsNullOrWhiteSpace(userId) ? null : userId;
    }

    static string? GetCurrentUrl(HttpRequest request)
    {
        if (string.IsNullOrEmpty(request.Scheme) || !request.Host.HasValue)
        {
            return null;
        }

        return string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent(),
            request.Path.ToUriComponent(),
            request.QueryString.ToUriComponent());
    }

    static string? GetRequestPath(HttpRequest request)
    {
        var path = string.Concat(request.PathBase.ToUriComponent(), request.Path.ToUriComponent());
        return string.IsNullOrEmpty(path) ? null : path;
    }

    static string? GetClientIp(HttpContext httpContext)
    {
        var forwardedFor = SanitizeHeaderValue(httpContext.Request.Headers[XForwardedFor]);
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var firstIp = forwardedFor.Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(firstIp))
            {
                return firstIp;
            }
        }

        return httpContext.Connection.RemoteIpAddress?.ToString();
    }
}

/// <summary>
/// Sanitized PostHog tracing context extracted from an ASP.NET Core request.
/// </summary>
/// <param name="DistinctId">The analytics distinct ID to apply to captures without an explicit distinct ID.</param>
/// <param name="SessionId">The session ID to apply as <c>$session_id</c> when captures do not provide one explicitly.</param>
/// <param name="Properties">Request metadata to add to captures before explicit capture properties.</param>
public sealed record PostHogTracingContext(
    string? DistinctId,
    string? SessionId,
    IReadOnlyDictionary<string, object> Properties);
