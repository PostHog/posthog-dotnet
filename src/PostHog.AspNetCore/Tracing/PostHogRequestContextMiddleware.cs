using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using PostHog.Api;
using PostHog.AspNetCore;
using PostHog.ErrorTracking;

namespace PostHog;

/// <summary>
/// ASP.NET Core middleware that applies PostHog request context to the current async request flow.
/// </summary>
internal sealed class PostHogRequestContextMiddleware(
    RequestDelegate next,
    IPostHogClient postHog,
    PostHogRequestContextOptions options)
{
    readonly RequestDelegate _next = next;
    readonly IPostHogClient _postHog = postHog;
    readonly PostHogRequestContextOptions _options = options;

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
            PostHogContext.Current!.SpanEnrichmentEnabled = _postHog switch
            {
                PostHogClient client => !client.IsDisabled,
                Sdk.NoOpPostHogClient => false,
                _ => true,
            };
            PostHogActivityContext.Apply(Activity.Current);
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
                    GetExceptionStatusCode(httpContext),
                    EscapedRequestBoundary);

                throw;
            }

            // A downstream error handler (UseExceptionHandler, the developer exception page) may have consumed
            // the failure and written an error response, so nothing escaped to the catch block above.
            if (GetConsumedException(httpContext) is { } consumed)
            {
                TryCaptureException(
                    _postHog,
                    consumed,
                    GetExceptionStatusCode(httpContext),
                    ConsumedRequestBoundary);
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

    // The failure escaped the request boundary: nothing downstream turned it into a response.
    static readonly ExceptionCaptureBoundary EscapedRequestBoundary = new(
        mechanismType: "middleware",
        source: "aspnetcore.request_context_middleware",
        handled: false);

    // A downstream framework handler consumed the failure and produced the response, so it counts as handled.
    static readonly ExceptionCaptureBoundary ConsumedRequestBoundary = new(
        mechanismType: "middleware",
        source: "aspnetcore.request_context_middleware",
        handled: true);

    static Exception? GetConsumedException(HttpContext httpContext)
    {
        try
        {
            return httpContext.Features.Get<IExceptionHandlerFeature>()?.Error;
        }
#pragma warning disable CA1031 // Reading request features must not fail a request that already has a response.
        catch
#pragma warning restore CA1031
        {
            return null;
        }
    }

    static void TryCaptureException(
        IPostHogClient postHog,
        Exception exception,
        int statusCode,
        ExceptionCaptureBoundary boundary)
    {
        try
        {
            // The boundary travels with the call rather than through the client type, so decorated or wrapped
            // IPostHogClient registrations still report the request failure as unhandled.
            using (ExceptionCaptureBoundary.Enter(exception, boundary))
            {
                postHog.CaptureException(exception, GetExceptionProperties(statusCode));
            }
        }
#pragma warning disable CA1031 // PostHog must not replace the host application's original exception.
        catch
#pragma warning restore CA1031
        {
            // Swallow SDK/client failures so the request outcome is never changed by exception capture.
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
        => new() { [PostHogProperties.ResponseStatusCode] = statusCode };
}
