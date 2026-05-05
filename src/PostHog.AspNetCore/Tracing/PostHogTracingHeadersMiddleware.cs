using Microsoft.AspNetCore.Http;
using PostHog.Api;

namespace PostHog;

/// <summary>
/// ASP.NET Core middleware that applies PostHog tracing headers to the current async request context.
/// </summary>
internal sealed class PostHogTracingHeadersMiddleware(
    RequestDelegate next,
    IPostHogClient postHog,
    PostHogTracingHeadersOptions options)
{
    readonly PostHogTracingHeadersOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Runs the downstream ASP.NET Core pipeline in a request-local <see cref="PostHogContext" />.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var tracingContext = PostHogTracingHeaders.Extract(httpContext, _options);
        using (PostHogContext.BeginScope(
                   tracingContext.DistinctId,
                   tracingContext.SessionId,
                   tracingContext.Properties,
                   fresh: true))
        {
            if (!_options.CaptureExceptions)
            {
                await next(httpContext);
                return;
            }

            try
            {
                await next(httpContext);
            }
#pragma warning disable CA1031 // Capture unhandled framework exceptions, then rethrow.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                postHog.CaptureException(exception, GetExceptionProperties(httpContext));
                throw;
            }
        }
    }

    static Dictionary<string, object> GetExceptionProperties(HttpContext httpContext)
    {
        var statusCode = httpContext.Response.StatusCode;
        if (statusCode < StatusCodes.Status400BadRequest)
        {
            statusCode = StatusCodes.Status500InternalServerError;
        }

        return new Dictionary<string, object> { [PostHogProperties.ResponseStatusCode] = statusCode };
    }
}
