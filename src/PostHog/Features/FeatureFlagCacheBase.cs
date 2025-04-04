using PostHog;
using PostHog.Api;
using PostHog.Features;

namespace PostHog.Features;

public abstract class FeatureFlagCacheBase : IFeatureFlagCache
{
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

        var results = await GetAndCacheFlagsAsync(distinctId, resultsFetcher, cancellationToken);
        return results.Flags;
    }

    public abstract Task<FlagsResult> GetAndCacheFlagsAsync(
        string distinctId,
        Func<string, CancellationToken, Task<FlagsResult>> fetcher,
        CancellationToken cancellationToken);
}