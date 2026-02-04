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
    /// <remarks>
    /// This method is obsolete because it does not include person properties and groups in the cache key,
    /// which can lead to incorrect cached results. Use the overload that accepts person properties and groups instead.
    /// </remarks>
    /// <param name="distinctId">The distinct id. Used as a cache key.</param>
    /// <param name="fetcher">The feature flag fetcher.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The set of feature flags.</returns>
    [Obsolete("Use GetAndCacheFlagsAsync overload that accepts personProperties and groups to ensure correct cache keys. This method will be removed in a future version.")]
#if !NETSTANDARD2_0
    async 
#endif 
    Task<FlagsResult> GetAndCacheFlagsAsync(
        string distinctId,
        Func<string, CancellationToken, Task<FlagsResult>> fetcher,
        CancellationToken cancellationToken)
#if !NETSTANDARD2_0
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
#else
    ;
#endif

    /// <summary>
    /// Attempts to retrieve the flags API result. If the feature flags are not in the cache, then
    /// they are fetched and stored in the cache.
    /// </summary>
    /// <remarks>
    /// Default implementation calls the fetcher directly without caching to avoid incorrect cache keys.
    /// Implementations should override this method to provide proper caching using all parameters.
    /// </remarks>
    /// <param name="distinctId">The distinct id.</param>
    /// <param name="personProperties">Optional person properties that affect feature flag evaluation.</param>
    /// <param name="groups">Optional groups with their properties that affect feature flag evaluation.</param>
    /// <param name="fetcher">The feature flag fetcher.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The set of feature flags.</returns>
#if !NETSTANDARD2_0
    async 
#endif 
    Task<FlagsResult> GetAndCacheFlagsAsync(
        string distinctId,
        IReadOnlyDictionary<string, object?>? personProperties,
        GroupCollection? groups,
        Func<string, CancellationToken, Task<FlagsResult>> fetcher,
        CancellationToken cancellationToken)
#if !NETSTANDARD2_0
    {
        // Default implementation: no caching to avoid incorrect cache keys
        // Implementations should override this to provide proper caching
        return await NotNull(fetcher)(distinctId, cancellationToken);
    }
#else
    ;
#endif
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

    /// <inheritdoc/>
    public Task<FlagsResult> GetAndCacheFlagsAsync(
        string distinctId,
        IReadOnlyDictionary<string, object?>? personProperties,
        GroupCollection? groups,
        Func<string, CancellationToken, Task<FlagsResult>> fetcher,
        CancellationToken cancellationToken)
        => NotNull(fetcher)(distinctId, cancellationToken);
}