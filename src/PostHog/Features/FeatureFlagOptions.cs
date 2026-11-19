namespace PostHog;

/// <summary>
/// Options to control how feature flags are evaluated.
/// </summary>
public class FeatureFlagOptions : AllFeatureFlagsOptions
{
    /// <summary>
    /// Whether to send the $feature_flag_called event when evaluating a feature flag.
    /// Used for Experiments. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// When <c>true</c>, the $feature_flag_called event is sent to PostHog when calling
    /// <see cref="IPostHogClient.GetFeatureFlagAsync"/>.
    /// </remarks>
    public bool SendFeatureFlagEvents { get; init; } = true;
}

/// <summary>
/// Options for retrieving all feature flags.
/// </summary>
public class AllFeatureFlagsOptions
{
    /// <summary>
    /// Whether to only evaluate flags locally. Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Local evaluation requires that <see cref="PostHogOptions.SecretKey"/> is set. When this is
    /// <c>true</c>, no <c>/flags</c> request is made and flags that cannot be resolved locally are
    /// absent from the result.
    /// </remarks>
    public bool OnlyEvaluateLocally { get; init; }

    /// <summary>
    /// The set of person properties used to evaluate feature flags. Required for both local and remote evaluation
    /// when feature flags have conditions based on person properties.
    /// For local evaluation (when <see cref="PostHogOptions.SecretKey"/> is present and
    /// <see cref="OnlyEvaluateLocally"/> is <c>true</c>), these properties are used directly.
    /// For remote evaluation, these properties are sent to PostHog's servers and can override stored person properties.
    /// </summary>
    public Dictionary<string, object?>? PersonProperties { get; init; }

    /// <summary>
    /// A list of the currently active groups. Required if the flag depends on groups. Each group can optionally
    /// include properties that override what's on PostHog's server when evaluating feature flags.
    /// Specifying properties for each group is required if <see cref="OnlyEvaluateLocally"/> is <c>true</c>.
    /// </summary>
    public GroupCollection? Groups { get; init; }

    /// <summary>
    /// Person property keys that you know are absent for this person. Optional.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Local evaluation treats a listed key as absent, even when <see cref="PersonProperties"/> holds a value for it.
    /// <c>is_not_set</c> and the negated operators (<c>is_not</c>, <c>not_icontains</c>, <c>not_starts_with</c>,
    /// <c>not_ends_with</c>, <c>not_regex</c>) match, and every other operator does not, so the flag resolves
    /// locally without a <c>/flags</c> request. A key that is neither supplied nor listed still needs the
    /// <c>/flags</c> fallback. Keys that start with <c>__posthog_person_metadata__</c> are ignored locally.
    /// </para>
    /// <para>
    /// When the SDK calls <c>/flags</c>, it sends the listed keys as <c>person_property_keys_known_absent</c>. Calls
    /// that list any key skip the <see cref="IFeatureFlagCache"/> and always make a fresh <c>/flags</c> request.
    /// </para>
    /// <para>
    /// The list can hold at most 1000 keys, counting duplicates. A longer list throws an
    /// <see cref="ArgumentException"/>, and the SDK doesn't evaluate or send a request.
    /// </para>
    /// </remarks>
    public IReadOnlyCollection<string>? PersonPropertyKeysKnownAbsent { get; init; }

    /// <summary>
    /// Group property keys that you know are absent, by group type. Optional.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each key applies only to its group type, and otherwise works like <see cref="PersonPropertyKeysKnownAbsent"/>.
    /// A listed key is absent even when the group's properties hold a value for it, including <c>$group_key</c>.
    /// When the SDK calls <c>/flags</c>, it sends the listed keys as <c>group_property_keys_known_absent</c>.
    /// Calls that list any key skip the <see cref="IFeatureFlagCache"/> and always make a fresh <c>/flags</c> request.
    /// </para>
    /// <para>
    /// Each group type's list can hold at most 1000 keys, counting duplicates. A longer list throws an
    /// <see cref="ArgumentException"/>, and the SDK doesn't evaluate or send a request.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>>? GroupPropertyKeysKnownAbsent { get; init; }

    /// <summary>
    /// The set of flag keys to evaluate in this request. If omitted or <c>null</c>, all flags are evaluated.
    /// An empty list evaluates no flags.
    /// </summary>
    /// <remarks>
    /// This scopes local evaluation, the <c>/flags</c> request, and the returned result. An empty list
    /// returns an empty snapshot without consulting caches, local definitions, or <c>/flags</c>. A requested
    /// key missing from local definitions triggers remote fallback unless <see cref="OnlyEvaluateLocally"/>
    /// is <c>true</c>. When <c>EvaluateFlagsAsync</c> falls back with a non-empty scope, it bypasses
    /// the general feature flag cache, so a key absent remotely costs one request per call.
    /// </remarks>
    public IReadOnlyList<string>? FlagKeysToEvaluate { get; init; }

    /// <summary>
    /// Whether to disable GeoIP enrichment for the feature flag request. Defaults to <c>false</c>.
    /// </summary>
    public bool DisableGeoIp { get; init; }
}