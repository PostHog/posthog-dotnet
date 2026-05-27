using PostHog;
using PostHog.Api;
using PostHog.Features;

namespace PostHog.Features;

/// <summary>
/// Base implementation of <see cref="IFeatureFlagCache"/> that adapts older feature-flag cache methods to the current flags-result API.
/// </summary>
public abstract class FeatureFlagCacheBase : IFeatureFlagCache
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, FeatureFlag>> GetAndCacheFeatureFlagsAsync(
        string distinctId,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, FeatureFlag>>> fetcher,
        CancellationToken cancellationToken)
    {
        var resultsFetcher = async (string id, CancellationToken ct) =>
        {
            var flags = await fetcher(ct);
            return new FlagsResult { Flags = flags };
        };

        var results = await GetAndCacheFlagsAsync(distinctId, null, null, resultsFetcher, cancellationToken);
        return results.Flags;
    }

    /// <inheritdoc />
    public async Task<FlagsResult> GetAndCacheFlagsAsync(
        string distinctId,
        Func<string, CancellationToken, Task<FlagsResult>> fetcher,
        CancellationToken cancellationToken)
    {
        // Call the new method with null properties for backward compatibility
        return await GetAndCacheFlagsAsync(distinctId, null, null, fetcher, cancellationToken);
    }

    /// <inheritdoc />
    public abstract Task<FlagsResult> GetAndCacheFlagsAsync(
        string distinctId,
        IReadOnlyDictionary<string, object?>? personProperties,
        GroupCollection? groups,
        Func<string, CancellationToken, Task<FlagsResult>> fetcher,
        CancellationToken cancellationToken);
}