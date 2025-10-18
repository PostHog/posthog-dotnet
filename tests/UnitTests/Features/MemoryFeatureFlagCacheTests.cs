using PostHog;
using PostHog.Api;
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

public class TheGetAndCacheFlagsAsyncMethodWithPropertiesAndGroups
{
    [Fact]
    public async Task ReturnsCachedFlagsWhenAllParametersMatch()
    {
        var timeProvider = TimeProvider.System;
        var cache = new MemoryFeatureFlagCache(timeProvider, 100, 0.1);
        var distinctId = "test-user";
        var personProperties = new Dictionary<string, object?> { ["email"] = "test@example.com" };
        var groups = new GroupCollection { { "company", "acme" } };

        var expectedFlags = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
            }
        };

        var fetchCount = 0;
        var fetcher = (string id, CancellationToken ct) =>
        {
            fetchCount++;
            return Task.FromResult(expectedFlags);
        };

        // First call should fetch
        await cache.GetAndCacheFlagsAsync(distinctId, personProperties, groups, fetcher, CancellationToken.None);
        Assert.Equal(1, fetchCount);

        // Second call with same parameters should use cache
        var result = await cache.GetAndCacheFlagsAsync(distinctId, personProperties, groups, fetcher, CancellationToken.None);
        Assert.Equal(1, fetchCount); // Should not fetch again
        Assert.Equal(expectedFlags.Flags, result.Flags);
    }

    [Fact]
    public async Task FetchesNewFlagsWhenPersonPropertiesDiffer()
    {
        var timeProvider = TimeProvider.System;
        var cache = new MemoryFeatureFlagCache(timeProvider, 100, 0.1);
        var distinctId = "test-user";
        var personProperties1 = new Dictionary<string, object?> { ["email"] = "test1@example.com" };
        var personProperties2 = new Dictionary<string, object?> { ["email"] = "test2@example.com" };

        var flags1 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
            }
        };

        var flags2 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = false } }
            }
        };

        var fetchCount = 0;
        var fetcher = (string id, CancellationToken ct) =>
        {
            fetchCount++;
            return Task.FromResult(fetchCount == 1 ? flags1 : flags2);
        };

        var result1 = await cache.GetAndCacheFlagsAsync(distinctId, personProperties1, null, fetcher, CancellationToken.None);
        var result2 = await cache.GetAndCacheFlagsAsync(distinctId, personProperties2, null, fetcher, CancellationToken.None);

        Assert.Equal(2, fetchCount); // Should fetch twice because properties differ
        Assert.NotEqual(result1.Flags, result2.Flags);
    }

    [Fact]
    public async Task FetchesNewFlagsWhenGroupsDiffer()
    {
        var timeProvider = TimeProvider.System;
        var cache = new MemoryFeatureFlagCache(timeProvider, 100, 0.1);
        var distinctId = "test-user";
        var groups1 = new GroupCollection { { "company", "acme" } };
        var groups2 = new GroupCollection { { "company", "initech" } };

        var flags1 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
            }
        };

        var flags2 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = false } }
            }
        };

        var fetchCount = 0;
        var fetcher = (string id, CancellationToken ct) =>
        {
            fetchCount++;
            return Task.FromResult(fetchCount == 1 ? flags1 : flags2);
        };

        var result1 = await cache.GetAndCacheFlagsAsync(distinctId, null, groups1, fetcher, CancellationToken.None);
        var result2 = await cache.GetAndCacheFlagsAsync(distinctId, null, groups2, fetcher, CancellationToken.None);

        Assert.Equal(2, fetchCount); // Should fetch twice because groups differ
        Assert.NotEqual(result1.Flags, result2.Flags);
    }

    [Fact]
    public async Task FetchesNewFlagsWhenGroupPropertiesDiffer()
    {
        var timeProvider = TimeProvider.System;
        var cache = new MemoryFeatureFlagCache(timeProvider, 100, 0.1);
        var distinctId = "test-user";
        var groups1 = new GroupCollection
        {
            new Group("company", "acme") { ["tier"] = "enterprise" }
        };
        var groups2 = new GroupCollection
        {
            new Group("company", "acme") { ["tier"] = "starter" }
        };

        var flags1 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
            }
        };

        var flags2 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = false } }
            }
        };

        var fetchCount = 0;
        var fetcher = (string id, CancellationToken ct) =>
        {
            fetchCount++;
            return Task.FromResult(fetchCount == 1 ? flags1 : flags2);
        };

        var result1 = await cache.GetAndCacheFlagsAsync(distinctId, null, groups1, fetcher, CancellationToken.None);
        var result2 = await cache.GetAndCacheFlagsAsync(distinctId, null, groups2, fetcher, CancellationToken.None);

        Assert.Equal(2, fetchCount); // Should fetch twice because group properties differ
        Assert.NotEqual(result1.Flags, result2.Flags);
    }

    [Fact]
    public async Task FetchesNewFlagsWhenNullVsNonNullProperties()
    {
        var timeProvider = TimeProvider.System;
        var cache = new MemoryFeatureFlagCache(timeProvider, 100, 0.1);
        var distinctId = "test-user";
        var personProperties = new Dictionary<string, object?> { ["email"] = "test@example.com" };

        var flags1 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
            }
        };

        var flags2 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = false } }
            }
        };

        var fetchCount = 0;
        var fetcher = (string id, CancellationToken ct) =>
        {
            fetchCount++;
            return Task.FromResult(fetchCount == 1 ? flags1 : flags2);
        };

        var result1 = await cache.GetAndCacheFlagsAsync(distinctId, null, null, fetcher, CancellationToken.None);
        var result2 = await cache.GetAndCacheFlagsAsync(distinctId, personProperties, null, fetcher, CancellationToken.None);

        Assert.Equal(2, fetchCount); // Should fetch twice because one has properties and one doesn't
        Assert.NotEqual(result1.Flags, result2.Flags);
    }
}