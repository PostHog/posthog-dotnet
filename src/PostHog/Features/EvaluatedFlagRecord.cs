namespace PostHog.Features;

/// <summary>
/// The internal per-flag record stored on a <see cref="FeatureFlagEvaluations"/> snapshot. Captures
/// everything required to (a) attach event properties when the snapshot is forwarded to <c>Capture</c>
/// and (b) fire a fully-populated <c>$feature_flag_called</c> event on first access.
/// </summary>
internal sealed record EvaluatedFlagRecord
{
    public required string Key { get; init; }

    /// <summary>
    /// The underlying <see cref="FeatureFlag"/> as exposed to callers via
    /// <see cref="FeatureFlagEvaluations.GetFlag"/>.
    /// </summary>
    public required FeatureFlag Flag { get; init; }

    /// <summary>
    /// Whether the flag is enabled. Mirrors <c>Flag.IsEnabled</c> but stored explicitly so the snapshot
    /// can compute <c>$active_feature_flags</c> without re-traversing <see cref="FeatureFlag"/>.
    /// </summary>
    public required bool Enabled { get; init; }

    /// <summary>
    /// The string-form value used as the dedup-cache key for <c>$feature_flag_called</c>. Derived
    /// from the implicit <see cref="FeatureFlag"/>-to-<see cref="string"/> conversion so the legacy
    /// single-flag path and the snapshot path produce byte-identical cache keys.
    /// </summary>
    public required string CacheKeyValue { get; init; }

    public int? Id { get; init; }
    public int? Version { get; init; }
    public string? Reason { get; init; }
    public bool LocallyEvaluated { get; init; }
}
