namespace PostHog;

/// <summary>
/// Options to control how feature flags are sent with captured events.
/// </summary>
public class SendFeatureFlagsOptions
{
    /// <summary>
    /// Whether to only evaluate the flag locally. Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Local evaluation requires that <see cref="PostHogOptions.PersonalApiKey"/> is set.
    /// </remarks>
    public bool OnlyEvaluateLocally { get; init; }

    /// <summary>
    /// The set of person properties to use for feature flag evaluation. 
    /// If evaluating locally, these properties are used for flag computation.
    /// If evaluating remotely, these can override person properties on PostHog's servers.
    /// </summary>
    public Dictionary<string, object?>? PersonProperties { get; init; }

    /// <summary>
    /// A dictionary of group properties to use for feature flag evaluation.
    /// The key is the group type and the value is a dictionary of properties for that group.
    /// Required if the flag depends on groups and <see cref="OnlyEvaluateLocally"/> is <c>true</c>.
    /// </summary>
    public Dictionary<string, Dictionary<string, object?>>? GroupProperties { get; init; }
}