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
    readonly RequestDelegate _next = next ?? (_ => Task.CompletedTask);
    readonly IPostHogClient? _postHog = postHog;
    readonly PostHogRequestContextOptions _options = options ?? new PostHogRequestContextOptions();

    /// <summary>
    /// Runs the downstream ASP.NET Core pipeline in a request-local PostHog context.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return;
        }

        var requestContext = ExtractRequestContext(httpContext, _options);
        using (PostHogContext.BeginScope(
                   requestContext.DistinctId,
                   requestContext.SessionId,
                   requestContext.Properties,
                   fresh: true))
        {
            if (!_options.CaptureExceptions)
            {
                await _next(httpContext);
                return;
            }

            try
            {
                await _next(httpContext);
            }
#pragma warning disable CA1031 // Capture unhandled framework exceptions, then rethrow.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                TryCaptureException(
                    _postHog,
                    exception,
                    GetExceptionStatusCode(httpContext));

                throw;
            }
        }
    }

    static PostHogRequestContextData ExtractRequestContext(
        HttpContext httpContext,
        PostHogRequestContextOptions options)
    {
        try
        {
            return PostHogTracingHeaders.Extract(httpContext, options);
        }
#pragma warning disable CA1031 // Request metadata parsing must not crash the host app.
        catch
#pragma warning restore CA1031
        {
            return new PostHogRequestContextData(null, null, new Dictionary<string, object>(0));
        }
    }

    static void TryCaptureException(
        IPostHogClient? postHog,
        Exception exception,
        int statusCode)
    {
        try
        {
            if (postHog is null)
            {
                return;
            }

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
