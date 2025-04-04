using Microsoft.Extensions.Caching.Memory;
using PostHog.Api;
using PostHog.Features;
using PostHog.Library;

namespace PostHog;

/// <summary>
/// Caches feature flags using a <see cref="MemoryCache"/>.
/// </summary>
/// <param name="timeProvider">The time provider to use for the cache.</param>
/// <param name="sizeLimit">The size limit of the cache. In this case, the number of entries allowed.</param>
/// <param name="compactPercentage">The amount (as a percentage) the cache should be compacted when it reaches its size limit.</param>
public sealed class MemoryFeatureFlagCache(TimeProvider timeProvider, int sizeLimit, double compactPercentage) : FeatureFlagCacheBase, IDisposable
{
    readonly MemoryCache _cache = new(new MemoryCacheOptions
    {
        SizeLimit = sizeLimit,
        Clock = new TimeProviderSystemClock(timeProvider),
        CompactionPercentage = compactPercentage
    });

    /// <inherititdoc/>
    public override async Task<FlagsResult> GetAndCacheFlagsAsync(
        string distinctId,
        Func<string, CancellationToken, Task<FlagsResult>> fetcher,
        CancellationToken cancellationToken)
    {
        var flags = await _cache.GetOrCreateAsync(
            distinctId,
            async cacheEntry =>
            {
                cacheEntry.SetSize(1);
                cacheEntry.SetPriority(CacheItemPriority.High);
                cacheEntry.SetAbsoluteExpiration(TimeSpan.FromSeconds(10));
                return await fetcher(distinctId, cancellationToken);
            });
        return flags ?? new FlagsResult();
    }

    public void Dispose() => _cache.Dispose();
}