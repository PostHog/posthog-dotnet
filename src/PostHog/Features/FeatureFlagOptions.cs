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
    /// Whether to only evaluate the flag locally. Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Local evaluation requires that <see cref="PostHogOptions.PersonalApiKey"/> is set.
    /// </remarks>
    public bool OnlyEvaluateLocally { get; init; }

    /// <summary>
    /// The set of person properties used to evaluate feature flags. Required for both local and remote evaluation
    /// when feature flags have conditions based on person properties.
    /// For local evaluation (when <see cref="PostHogOptions.PersonalApiKey"/> is present and
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
    /// The set of flag keys to evaluate in this request. If not specified, all flags are evaluated.
    /// </summary>
    public IReadOnlyList<string> FlagKeysToEvaluate { get; init; } = [];
}