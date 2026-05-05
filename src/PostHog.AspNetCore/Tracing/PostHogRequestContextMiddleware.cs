using Microsoft.AspNetCore.Http;

namespace PostHog;

/// <summary>
/// ASP.NET Core middleware that applies PostHog request context to the current async request flow.
/// </summary>
internal sealed class PostHogRequestContextMiddleware(
    RequestDelegate next,
    IPostHogClient postHog,
    PostHogRequestContextOptions options)
{
    readonly PostHogRequestContextOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Runs the downstream ASP.NET Core pipeline in a request-local <see cref="PostHogContext" />.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var requestContext = PostHogTracingHeaders.Extract(httpContext, _options);
        using (PostHogContext.BeginScope(
                   requestContext.DistinctId,
                   requestContext.SessionId,
                   requestContext.Properties,
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
                var statusCode = GetExceptionStatusCode(httpContext);
                if (statusCode >= _options.MinimumExceptionStatusCode)
                {
                    TryCaptureException(postHog, exception, statusCode);
                }

                throw;
            }
        }
    }

    static void TryCaptureException(
        IPostHogClient postHog,
        Exception exception,
        int statusCode)
    {
        try
        {
            postHog.CaptureException(exception, GetExceptionProperties(statusCode));
        }
#pragma warning disable CA1031 // PostHog must not replace the host application's original exception.
        catch
#pragma warning restore CA1031
        {
            // Swallow SDK/client failures so the original downstream exception is preserved.
        }
    }

    static int GetExceptionStatusCode(HttpContext httpContext)
    {
        var statusCode = httpContext.Response.StatusCode;
        return statusCode < StatusCodes.Status400BadRequest
            ? StatusCodes.Status500InternalServerError
            : statusCode;
    }

    static Dictionary<string, object> GetExceptionProperties(int statusCode)
        => new() { [PostHogRequestPropertyNames.ResponseStatusCode] = statusCode };
}
