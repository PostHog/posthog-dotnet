namespace PostHog.Prompts;

/// <summary>
/// Options for <see cref="IPostHogPrompts.GetAsync"/>.
/// </summary>
public sealed class GetPromptOptions
{
    /// <summary>
    /// The prompt version to fetch. Cannot be combined with <see cref="Label"/>. When neither is set, the latest
    /// version is fetched.
    /// </summary>
    public int? Version { get; init; }

    /// <summary>
    /// Fetch the version this label points to, such as <c>production</c>. Cannot be combined with
    /// <see cref="Version"/>.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// Prompt text to return when the fetch fails and nothing is cached. Without it, the failure is thrown as a
    /// <see cref="PromptFetchException"/>.
    /// </summary>
    public string? Fallback { get; init; }

    /// <summary>
    /// How long a fetched prompt is served from the cache for this call. Defaults to
    /// <see cref="PostHogOptions.PromptCacheTtl"/>. <see cref="TimeSpan.Zero"/> fetches on every call.
    /// </summary>
    public TimeSpan? CacheTtl { get; init; }
}
