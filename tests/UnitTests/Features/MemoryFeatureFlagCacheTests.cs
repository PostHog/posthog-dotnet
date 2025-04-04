using PostHog;
using PostHog.Features;

namespace InMemoryFeatureFlagCacheTests;

public class TheGetAndCacheFeatureFlagsAsyncMethod
{
    [Fact]
    public async Task ReturnsCachedFlagsWhenFlagsAreCached()
    {
        var timeProvider = TimeProvider.System;
        var cache = new MemoryFeatureFlagCache(timeProvider, 100, 0.1);
        var distinctId = "test-distinct-id";
        var expectedFlags = new Dictionary<string, FeatureFlag>
        {
            { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
        };
        await cache.GetAndCacheFeatureFlagsAsync(distinctId, _ => Task.FromResult((IReadOnlyDictionary<string, FeatureFlag>)expectedFlags), CancellationToken.None);

        var result = await cache.GetAndCacheFeatureFlagsAsync(
            distinctId,
            _ => Task.FromResult<IReadOnlyDictionary<string, FeatureFlag>>(new Dictionary<string, FeatureFlag>()), CancellationToken.None);

        Assert.Equal(expectedFlags, result);
    }

    [Fact]
    public async Task FetchesAndCachesFlagsWhenFlagsAreNotCached()
    {
        var timeProvider = TimeProvider.System;
        var cache = new MemoryFeatureFlagCache(timeProvider, 100, 0.1);
        var distinctId = "test-distinct-id";
        var expectedFlags = new Dictionary<string, FeatureFlag>
        {
            { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
        };

        var result = await cache.GetAndCacheFeatureFlagsAsync(
            distinctId,
            _ => Task.FromResult<IReadOnlyDictionary<string, FeatureFlag>>(expectedFlags), CancellationToken.None);

        Assert.Equal(expectedFlags, result);
    }
}