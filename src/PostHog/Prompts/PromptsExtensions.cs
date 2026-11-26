using PostHog.Prompts;
using static PostHog.Library.Ensure;

namespace PostHog; // Intentionally put in the root namespace.

/// <summary>
/// Extension methods on <see cref="IPostHogPrompts"/>.
/// </summary>
public static class PromptsExtensions
{
    /// <summary>
    /// Fetches every prompt in one request and caches each one, so later <see cref="IPostHogPrompts.GetAsync"/>
    /// calls for them are served from the cache within the TTL. Call it once at startup instead of fetching prompts
    /// one by one.
    /// </summary>
    /// <param name="prompts">The prompts client, from <see cref="PostHogClient.Prompts"/> or dependency injection.</param>
    /// <param name="label">
    /// Fetch each prompt at the version this label points to, such as <c>production</c>. Prompts without the label
    /// are left out. Without a label, every prompt is fetched at its latest version.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The fetched prompts by name.</returns>
    /// <exception cref="PromptFetchException">
    /// The request failed, or the server does not support fetching prompts by label on the list endpoint (PostHog
    /// releases from before September 2026). The cache is left as it was.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="prompts"/> is not the prompts client of <see cref="PostHogClient"/>.
    /// </exception>
    public static Task<IReadOnlyDictionary<string, PromptResult>> GetAllAsync(
        this IPostHogPrompts prompts,
        string? label = null,
        CancellationToken cancellationToken = default)
        => NotNull(prompts) is PromptFetcher fetcher
            ? fetcher.GetAllAsync(label, cancellationToken)
            : throw new NotSupportedException(
                "GetAllAsync needs the prompts client of PostHogClient. This implementation of IPostHogPrompts cannot fetch prompts in bulk.");

    /// <summary>
    /// Removes cached prompts, so the next <see cref="IPostHogPrompts.GetAsync"/> for them fetches from PostHog.
    /// Use it when you know a prompt changed and cannot wait for the cache TTL.
    /// </summary>
    /// <param name="prompts">The prompts client, from <see cref="PostHogClient.Prompts"/> or dependency injection.</param>
    /// <param name="name">
    /// The prompt to remove, in every cached version and label. <c>null</c> removes every prompt.
    /// </param>
    /// <param name="version">Removes only this version of <paramref name="name"/>.</param>
    /// <exception cref="ArgumentException">A version was given without a name. Nothing was removed.</exception>
    /// <remarks>
    /// <para>
    /// This also ends the wait that follows a failed fetch. A request that is already running still answers
    /// the calls waiting for it, but its result is not cached. After a removal there is no cached version to
    /// serve when the next fetch fails.
    /// </para>
    /// <para>
    /// The cache belongs to the PostHog client. On another implementation of <see cref="IPostHogPrompts"/>,
    /// such as a test double, this method does nothing.
    /// </para>
    /// </remarks>
    public static void ClearCache(this IPostHogPrompts prompts, string? name = null, int? version = null)
    {
        if (NotNull(prompts) is PromptFetcher fetcher)
        {
            fetcher.ClearCache(name, version);
        }
    }
}
