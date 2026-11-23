namespace PostHog.Prompts;

/// <summary>
/// Fetches LLM prompts managed in PostHog, so prompt text and model settings can change without a deploy.
/// Reach it through <see cref="PostHogClient.Prompts"/>, or inject it when you register PostHog with
/// <c>AddPostHog</c>.
/// </summary>
/// <remarks>
/// Needs <see cref="PostHogOptions.SecretKey"/> set to a personal API key (<c>phx_...</c>) or a project secret
/// API key (<c>phs_...</c>) with the <c>llm_prompt:read</c> scope. Fetched prompts are cached for
/// <see cref="PostHogOptions.PromptCacheTtl"/>.
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
}
