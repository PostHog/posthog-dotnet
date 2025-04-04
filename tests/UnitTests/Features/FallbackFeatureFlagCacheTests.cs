using PostHog;
using PostHog.Features;

namespace FallbackFeatureFlagCacheTests;

public class TheGetAndCacheFeatureFlagsAsyncMethod
{
    [Fact]
    public async Task ReturnsItemInPrimaryCache()
    {
        var timeProvider = TimeProvider.System;
        var primaryCache = new MemoryFeatureFlagCache(timeProvider, 10, 0.2);
        var secondaryCache = new MemoryFeatureFlagCache(timeProvider, 10, 0.2);

        var cache = new FallbackFeatureFlagCache(primaryCache, secondaryCache);
        var distinctId = "test-distinct-id";
        var expectedFlags = new Dictionary<string, FeatureFlag>
        {
            { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
        };
        await primaryCache.GetAndCacheFeatureFlagsAsync(distinctId, _ => Task.FromResult<IReadOnlyDictionary<string, FeatureFlag>>(expectedFlags), CancellationToken.None);

        var result = await cache.GetAndCacheFeatureFlagsAsync(
            distinctId,
            _ => Task.FromResult<IReadOnlyDictionary<string, FeatureFlag>>(new Dictionary<string, FeatureFlag>()), CancellationToken.None);

        Assert.Equal(expectedFlags, result);
    }

    [Fact]
    public async Task ReturnsItemInSecondaryCache()
    {
        var timeProvider = TimeProvider.System;
        var primaryCache = new MemoryFeatureFlagCache(timeProvider, 10, 0.2);
        var secondaryCache = new MemoryFeatureFlagCache(timeProvider, 10, 0.2);

        var cache = new FallbackFeatureFlagCache(primaryCache, secondaryCache);
        var distinctId = "test-distinct-id";
        var expectedFlags = new Dictionary<string, FeatureFlag>
        {
            { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
        };
        await secondaryCache.GetAndCacheFeatureFlagsAsync(distinctId, _ => Task.FromResult<IReadOnlyDictionary<string, FeatureFlag>>(expectedFlags), CancellationToken.None);

        var result = await cache.GetAndCacheFeatureFlagsAsync(
            distinctId,
            _ => Task.FromResult<IReadOnlyDictionary<string, FeatureFlag>>(new Dictionary<string, FeatureFlag>()), CancellationToken.None);

        Assert.Equal(expectedFlags, result);
    }

}