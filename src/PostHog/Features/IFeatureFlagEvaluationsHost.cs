namespace PostHog.Features;

/// <summary>
/// The narrow seam between <see cref="FeatureFlagEvaluations"/> and the SDK client that owns the
/// dedup cache and logger. The snapshot only needs these two operations, so it does not depend on
/// the full <see cref="IPostHogClient"/> surface — keeping the snapshot simple and easy to test.
/// </summary>
internal interface IFeatureFlagEvaluationsHost
{
    /// <summary>
    /// Fires a <c>$feature_flag_called</c> event for the given access, deduplicated against the
    /// per-distinct-id cache that the legacy single-flag path also writes to.
    /// </summary>
    void TryCaptureFeatureFlagCalledEventIfNeeded(
        string distinctId,
        string featureKey,
        EvaluatedFlagRecord? record,
        GroupCollection? groups,
        string? requestId,
        long? evaluatedAt,
        long? flagDefinitionsLoadedAt,
        IReadOnlyCollection<string> errors);

    /// <summary>
    /// Logs a warning from <see cref="FeatureFlagEvaluations.OnlyAccessed"/> or
    /// <see cref="FeatureFlagEvaluations.Only(System.Collections.Generic.IEnumerable{string})"/>.
    /// Implementations should respect <see cref="PostHogOptions.FeatureFlagsLogWarnings"/>.
    /// </summary>
    void LogFilterWarning(string message);
}
