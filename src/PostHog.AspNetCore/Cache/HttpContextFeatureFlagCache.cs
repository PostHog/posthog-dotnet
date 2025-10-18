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
        return await GetAndCacheFlagsAsync(distinctId, null, null, fetcher, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<FlagsResult> GetAndCacheFlagsAsync(
        string distinctId,
        IReadOnlyDictionary<string, object?>? personProperties,
        GroupCollection? groups,
        Func<string, CancellationToken, Task<FlagsResult>> fetcher,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var cacheKey = GetCacheKey(distinctId, personProperties, groups);

        var flags = SafeGet(cacheKey);
        if (flags is null)
        {
            logger.LogTraceCacheMiss(cacheKey);
            flags = await NotNull(fetcher)(distinctId, cancellationToken);
            if (httpContext is null)
            {
                return flags;
            }
            logger.LogTraceStoringFeatureFlagsInCache(cacheKey);
            SafeSet(cacheKey, flags);
        }
        else
        {
            logger.LogTraceCacheHit(cacheKey);
        }

        return flags;
    }

    FlagsResult? SafeGet(string cacheKey)
    {
        var httpContext = httpContextAccessor.HttpContext;
        try
        {
            return httpContext?.Items[cacheKey] as FlagsResult;
        }
        catch (ObjectDisposedException ex)
        {
            logger.LogWarningRetrievingFeatureFlagsFromCacheFailed(ex, cacheKey);
            return null;
        }
    }

    void SafeSet(string cacheKey, FlagsResult flags)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        try
        {
            httpContext.Items[cacheKey] = flags;
        }
        catch (ObjectDisposedException ex)
        {
            logger.LogWarningStoringFeatureFlagsInCacheFailed(ex, cacheKey);
        }
    }

    static string GetCacheKey(
        string distinctId,
        IReadOnlyDictionary<string, object?>? personProperties,
        GroupCollection? groups)
        => $"$PostHog(feature_flags):{FeatureFlagCacheKey.Generate(distinctId, personProperties, groups)}";
}

internal static partial class HttpContextFeatureFlagCacheLoggerExtensions
{
    [LoggerMessage(
        EventId = 200,
        Level = LogLevel.Trace,
        Message = "✓ Cache HIT: Feature flags retrieved from HttpContext.Items using cache key '{cacheKey}'.")]
    public static partial void LogTraceCacheHit(this ILogger logger, string cacheKey);

    [LoggerMessage(
        EventId = 201,
        Level = LogLevel.Trace,
        Message = "✗ Cache MISS: Fetching feature flags for cache key '{cacheKey}'.")]
    public static partial void LogTraceCacheMiss(this ILogger logger, string cacheKey);

    [LoggerMessage(
        EventId = 202,
        Level = LogLevel.Trace,
        Message = "→ Storing feature flags in HttpContext.Items with cache key '{cacheKey}'.")]
    public static partial void LogTraceStoringFeatureFlagsInCache(this ILogger logger, string cacheKey);

    [LoggerMessage(
        EventId = 203,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve feature flags from HttpContext.Items for cache key '{cacheKey}'.")]
    public static partial void LogWarningRetrievingFeatureFlagsFromCacheFailed(
        this ILogger logger,
        Exception exception,
        string cacheKey);

    [LoggerMessage(
        EventId = 204,
        Level = LogLevel.Warning,
        Message = "Storing feature flags in HttpContext.Items failed for cache key '{cacheKey}'.")]
    public static partial void LogWarningStoringFeatureFlagsInCacheFailed(
        this ILogger logger,
        Exception exception,
        string cacheKey);
}
