using Microsoft.AspNetCore.Http;
using PostHog.Api;

namespace PostHog;

/// <summary>
/// ASP.NET Core middleware that applies PostHog tracing headers to the current async request context.
/// </summary>
public sealed class PostHogTracingHeadersMiddleware(
    RequestDelegate next,
    IPostHogClient postHog,
    bool captureExceptions = true)
{
    /// <summary>
    /// Runs the downstream ASP.NET Core pipeline in a request-local <see cref="PostHogContext" />.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var tracingContext = PostHogTracingHeaders.Extract(httpContext);
        using (PostHogContext.BeginScope(
                   tracingContext.DistinctId,
                   tracingContext.SessionId,
                   tracingContext.Properties,
                   fresh: true))
        {
            try
            {
                await next(httpContext);
            }
#pragma warning disable CA1031 // Capture unhandled framework exceptions, then rethrow.
            catch (Exception exception) when (captureExceptions)
#pragma warning restore CA1031
            {
                postHog.CaptureException(exception, GetExceptionProperties(httpContext));
                throw;
            }
        }
    }

    static Dictionary<string, object>? GetExceptionProperties(HttpContext httpContext)
    {
        var statusCode = httpContext.Response.StatusCode;
        return statusCode > 0
            ? new Dictionary<string, object> { [PostHogProperties.ResponseStatusCode] = statusCode }
            : null;
    }
}
