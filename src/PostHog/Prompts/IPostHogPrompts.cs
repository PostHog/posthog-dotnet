namespace PostHog.Prompts;

/// <summary>
/// Fetches LLM prompts managed in PostHog, so prompt text and model settings can change without a deploy.
/// Reach it through <see cref="PostHogClient.Prompts"/>, or inject it when you register PostHog with
/// <c>AddPostHog</c>.
/// </summary>
/// <remarks>
/// Needs <see cref="PostHogOptions.SecretKey"/> set to a personal API key (<c>phx_...</c>) with the
/// <c>llm_prompt:read</c> scope. Fetched prompts are cached for <see cref="PostHogOptions.PromptCacheTtl"/>.
/// When a fetch fails, the last cached version is served; when there is no cached version, the
/// <see cref="GetPromptOptions.Fallback"/> is returned, or a <see cref="PromptFetchException"/> is thrown.
/// </remarks>
public interface IPostHogPrompts
{
    /// <summary>
    /// Fetches a prompt by name. Without <see cref="GetPromptOptions.Version"/> or
    /// <see cref="GetPromptOptions.Label"/>, the latest version is returned.
    /// </summary>
    /// <param name="name">The prompt name, as shown in PostHog.</param>
    /// <param name="options">Optional: which version to fetch, the cache TTL for this call, and a fallback.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The prompt text with its version metadata and where it came from.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, or both a version and a label were given. No request was made.
    /// </exception>
    /// <exception cref="PromptFetchException">
    /// The prompt could not be fetched, nothing is cached for it, and no fallback was given.
    /// </exception>
    Task<PromptResult> GetAsync(string name, GetPromptOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes cached prompts, so the next <see cref="GetAsync"/> for them fetches from PostHog. Use it when
    /// you know a prompt changed and cannot wait for the cache TTL.
    /// </summary>
    /// <param name="name">
    /// The prompt to remove, in every cached version and label. <c>null</c> removes every prompt.
    /// </param>
    /// <param name="version">Removes only this version of <paramref name="name"/>.</param>
    /// <exception cref="ArgumentException">A version was given without a name. Nothing was removed.</exception>
    /// <remarks>
    /// This also ends the wait that follows a failed fetch. A request that is already running still answers
    /// the calls waiting for it, but its result is not cached. After a removal there is no cached version to
    /// serve when the next fetch fails.
    /// </remarks>
    void ClearCache(string? name = null, int? version = null);
}
