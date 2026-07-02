namespace PostHog.FeatureManagement;

/// <summary>
/// Shared fallback used by <see cref="PostHogFeatureDefinitionProvider"/> and
/// <see cref="PostHogVariantFeatureManager"/> to enumerate flag keys when no
/// <see cref="Config.PostHogOptions.PersonalApiKey"/> is configured (no local evaluation source).
/// </summary>
/// <remarks>
/// <para>
/// Polls <c>/flags</c> with a stable sentinel <c>distinct_id</c> and returns the keys from the
/// response. Per-flag values for the sentinel are discarded — only the set of keys is consumed.
/// </para>
/// <para>
/// The sentinel id is intentionally stable: a random id would spawn a phantom person on every poll
/// and defeat the in-memory <c>/flags</c> cache. See PostHog/posthog-dotnet#64 for the rationale.
/// </para>
/// </remarks>
internal static class FeatureEnumerationFallback
{
    /// <summary>
    /// Stable sentinel <c>distinct_id</c> used by the enumeration fallback. Stays constant so the
    /// SDK's in-memory <c>/flags</c> cache (keyed by distinct_id) reuses the response and PostHog
    /// doesn't create a new phantom person per poll.
    /// </summary>
    internal const string SentinelDistinctId = "$feature_enumeration_sentinel";

    public static async Task<IReadOnlyCollection<string>> GetFeatureKeysAsync(
        IPostHogClient posthog,
        CancellationToken cancellationToken)
    {
        var flags = await posthog.GetAllFeatureFlagsAsync(
            distinctId: SentinelDistinctId,
            options: null,
            cancellationToken);
        // Order is not guaranteed by /flags; callers (Microsoft Feature Management) don't rely on it.
        return flags.Keys.ToList();
    }
}
