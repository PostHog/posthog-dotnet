using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PostHog.Api;
using PostHog.Features;
using static PostHog.Library.Ensure;

namespace PostHog.Cache;

/// <summary>
/// An implementation of <see cref="IFeatureFlagCache"/> that uses the current <see cref="HttpContext"/> to cache
/// feature flags. If the <see cref="HttpContext"/> is not available, then the feature flags are not cached.
/// </summary>
public class HttpContextFeatureFlagCache(
    IHttpContextAccessor httpContextAccessor,
    ILogger<HttpContextFeatureFlagCache> logger) : IFeatureFlagCache
{
    /// <summary>
    /// Constructs a new instance of <see cref="HttpContextFeatureFlagCache"/> without a logger.
    /// </summary>
    /// <param name="httpContextAccessor"></param>
    public HttpContextFeatureFlagCache(IHttpContextAccessor httpContextAccessor)
        : this(httpContextAccessor, NullLogger<HttpContextFeatureFlagCache>.Instance)
    {
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, FeatureFlag>> GetAndCacheFeatureFlagsAsync(
        string distinctId,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, FeatureFlag>>> fetcher,
        CancellationToken cancellationToken)
    {
        var result = await GetAndCacheFlagsAsync(distinctId, async (_, ctx) =>
        {
            var flags = await NotNull(fetcher)(ctx);
            return new FlagsResult { Flags = flags };
        }, cancellationToken);
        return result.Flags;
    }

    /// <inheritdoc/>
    public async Task<FlagsResult> GetAndCacheFlagsAsync(
        string distinctId,
        Func<string, CancellationToken, Task<FlagsResult>> fetcher,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;

        var flags = SafeGet(distinctId);
        if (flags is null)
        {
            logger.LogTraceFetchingFeatureFlags(distinctId);
            flags = await NotNull(fetcher)(distinctId, cancellationToken);
            if (httpContext is null)
            {
                return flags;
            }
            logger.LogTraceStoringFeatureFlagsInCache(distinctId);
            SafeSet(distinctId, flags);
        }
        else
        {
            logger.LogTraceCacheHit(distinctId);
        }

        return flags;
    }

    FlagsResult? SafeGet(string distinctId)
    {
        var httpContext = httpContextAccessor.HttpContext;
        try
        {
            return httpContext?.Items[GetCacheKey(distinctId)] as FlagsResult;
        }
        catch (ObjectDisposedException ex)
        {
            logger.LogWarningRetrievingFeatureFlagsFromCacheFailed(ex, distinctId);
            return null;
        }
    }

    void SafeSet(string distinctId, FlagsResult flags)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        try
        {
            httpContext.Items[GetCacheKey(distinctId)] = flags;
        }
        catch (ObjectDisposedException ex)
        {
            logger.LogWarningStoringFeatureFlagsInCacheFailed(ex, distinctId);
        }
    }

    static string GetCacheKey(string distinctId) => $"$PostHog(feature_flags):{distinctId}";
}

internal static partial class HttpContextFeatureFlagCacheLoggerExtensions
{
    [LoggerMessage(
        EventId = 200,
        Level = LogLevel.Trace,
        Message = "Feature flags retrieved from HttpContext.Items for distinct ID '{distinctId}'.")]
    public static partial void LogTraceCacheHit(this ILogger logger, string distinctId);

    [LoggerMessage(
        EventId = 201,
        Level = LogLevel.Trace,
        Message = "Fetching feature flags for '{distinctId}'.")]
    public static partial void LogTraceFetchingFeatureFlags(this ILogger logger, string distinctId);

    [LoggerMessage(
        EventId = 202,
        Level = LogLevel.Trace,
        Message = "Storing feature flags in HttpContext.Items for '{distinctId}'.")]
    public static partial void LogTraceStoringFeatureFlagsInCache(this ILogger logger, string distinctId);

    [LoggerMessage(
        EventId = 203,
        Level = LogLevel.Warning,
        Message = "Fetching feature flags for '{distinctId}'.")]
    public static partial void LogWarningRetrievingFeatureFlagsFromCacheFailed(
        this ILogger logger,
        Exception exception,
        string distinctId);

    [LoggerMessage(
        EventId = 204,
        Level = LogLevel.Warning,
        Message = "Storing feature flags in HttpContext.Items failed for '{distinctId}'.")]
    public static partial void LogWarningStoringFeatureFlagsInCacheFailed(
        this ILogger logger,
        Exception exception,
        string distinctId);
}