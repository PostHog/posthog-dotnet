using PostHog.Api;
using PostHog.Features;
using static PostHog.Library.Ensure;

namespace PostHog;

/// <summary>
/// Used to cache feature flags for a duration appropriate to the environment.
/// </summary>
public interface IFeatureFlagCache
{
    /// <summary>
    /// Attempts to retrieve the feature flags from the cache. If the feature flags are not in the cache, then
    /// they are fetched and stored in the cache.
    /// </summary>
    /// <param name="distinctId">The distinct id. Used as a cache key.</param>
    /// <param name="fetcher">The feature flag fetcher.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The set of feature flags.</returns>
    [Obsolete("Use GetAndCacheFlagsAsync instead.")]
    Task<IReadOnlyDictionary<string, FeatureFlag>> GetAndCacheFeatureFlagsAsync(
        string distinctId,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, FeatureFlag>>> fetcher,
        CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to retrieve the flags API result. If the feature flags are not in the cache, then
    /// they are fetched and stored in the cache.
    /// </summary>
    /// <remarks>Default implementation uses the existing <see cref="GetAndCacheFeatureFlagsAsync"/> method.</remarks>
    /// <param name="distinctId">The distinct id. Used as a cache key.</param>
    /// <param name="fetcher">The feature flag fetcher.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The set of feature flags.</returns>
    async Task<FlagsResult> GetAndCacheFlagsAsync(
        string distinctId,
        Func<string, CancellationToken, Task<FlagsResult>> fetcher,
        CancellationToken cancellationToken)
    {
        return new FlagsResult
        {
#pragma warning disable CS0618 // Type or member is obsolete
            Flags = await GetAndCacheFeatureFlagsAsync(
#pragma warning restore CS0618 // Type or member is obsolete
                distinctId,
                async ctx =>
                {
                    var result = await fetcher(distinctId, ctx);
                    return result.Flags;
                },
                cancellationToken
            )
        };
    }
}

/// <summary>
/// A null cache that does not cache feature flags. It always calls the fetcher.
/// </summary>
public sealed class NullFeatureFlagCache : IFeatureFlagCache
{
    public static readonly NullFeatureFlagCache Instance = new();

    private NullFeatureFlagCache()
    {
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, FeatureFlag>> GetAndCacheFeatureFlagsAsync(
        string distinctId,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, FeatureFlag>>> fetcher,
        CancellationToken cancellationToken)
        => await NotNull(fetcher)(cancellationToken);

    /// <inheritdoc/>
    public Task<FlagsResult> GetAndCacheFlagsAsync(
        string distinctId,
        Func<string, CancellationToken, Task<FlagsResult>> fetcher,
        CancellationToken cancellationToken)
        => NotNull(fetcher)(distinctId, cancellationToken);
}

