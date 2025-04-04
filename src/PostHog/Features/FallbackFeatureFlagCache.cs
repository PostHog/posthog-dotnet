using PostHog.Api;
using PostHog.Library;

namespace PostHog.Features;

/// <summary>
/// Used to try and retrieve feature flags from a primary cache. If the primary cache fails, it falls back to a
/// secondary cache.
/// </summary>
/// <param name="primary">The primary cache.</param>
/// <param name="fallback">The secondary cache.</param>
public class FallbackFeatureFlagCache(IFeatureFlagCache primary, IFeatureFlagCache fallback) : FeatureFlagCacheBase
{
    readonly IFeatureFlagCache _primary = Ensure.NotNull(primary);
    readonly IFeatureFlagCache _fallback = Ensure.NotNull(fallback);

    /// <inherititdoc/>
    public override async Task<FlagsResult> GetAndCacheFlagsAsync(
        string distinctId,
        Func<string, CancellationToken, Task<FlagsResult>> fetcher,
        CancellationToken cancellationToken)
    {
        var flags = await _primary.GetAndCacheFlagsAsync(distinctId, fetcher, cancellationToken);
        if (flags.Flags.Count > 0)
        {
            return flags;
        }
        flags = await _fallback.GetAndCacheFlagsAsync(distinctId, fetcher, cancellationToken);
        if (flags.Flags.Count > 0)
        {
            return flags;
        }
        return new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>()
        };
    }
}