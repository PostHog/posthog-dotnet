namespace PostHog.Prompts;

/// <summary>
/// Where a <see cref="PromptResult"/> came from.
/// </summary>
public enum PromptSource
{
    /// <summary>
    /// Fetched from PostHog for this call.
    /// </summary>
    Api,

    /// <summary>
    /// Served from the cache within its TTL.
    /// </summary>
    Cache,

    /// <summary>
    /// The fetch failed, so the expired cached version was served.
    /// </summary>
    StaleCache,

    /// <summary>
    /// The fetch failed and nothing was cached, so <see cref="GetPromptOptions.Fallback"/> was returned.
    /// </summary>
    CodeFallback,
}
