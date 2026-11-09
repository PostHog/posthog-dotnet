using PostHog.Prompts;
using static PostHog.Library.Ensure;

namespace PostHog; // Intentionally put in the root namespace.

/// <summary>
/// Extension methods on <see cref="IPostHogPrompts"/>.
/// </summary>
public static class PromptsExtensions
{
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
