using System.Text.Json.Serialization;

namespace PostHog.Api;

/// <summary>
/// A feature flag as returned by the <c>/flags</c> endpoint.
/// </summary>
internal record FeatureFlagResult
{
    /// <summary>
    /// The key of the feature flag.
    /// </summary>
    public string Key { get; init; } = null!;

    /// <summary>
    /// Indicates whether the feature flag is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The variant of the feature flag, if any.
    /// </summary>
    public string? Variant { get; init; }

    /// <summary>
    /// The reason for the evaluation of the feature flag.
    /// </summary>
    public EvaluationReason Reason { get; init; } = null!;

    /// <summary>
    /// Metadata associated with the feature flag.
    /// </summary>
    public FeatureFlagMetadata Metadata { get; init; } = null!;

    /// <summary>
    /// Whether the flag failed evaluation due to a transient error.
    /// </summary>
    public bool? Failed { get; init; }
}

/// <summary>
/// The reason for the evaluation of the feature flag.
/// </summary>
internal record EvaluationReason
{
    /// <summary>
    /// The code representing the reason.
    /// </summary>
    public string? Code { get; init; } = null!;

    /// <summary>
    /// A description of the reason.
    /// </summary>
    public string? Description { get; init; } = null!;

    /// <summary>
    /// The index of the condition that matched.
    /// </summary>
    [JsonPropertyName("condition_index")]
    public int? ConditionIndex { get; init; }
}

/// <summary>
/// Metadata associated with the feature flag.
/// </summary>
internal record FeatureFlagMetadata
{
    /// <summary>
    /// The ID of the feature flag.
    /// </summary>
    public int? Id { get; init; }

    /// <summary>
    /// The version of the feature flag.
    /// </summary>
    public int? Version { get; init; }

    /// <summary>
    /// The payload of the feature flag.
    /// </summary>
    public string? Payload { get; init; }

    /// <summary>
    /// A description of the feature flag.
    /// </summary>
    public string? Description { get; init; }
}