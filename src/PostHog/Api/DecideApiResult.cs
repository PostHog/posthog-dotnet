using PostHog.Features;
using PostHog.Json;
using PostHog.Library;

namespace PostHog.Api;

/// <summary>
/// The union of the API Result for the <c>/decide?v=3</c> and <c>/decide?v=4</c>endpoint.
/// Some properties are omitted because they are not necessary for server-side scenarios.
/// When making a decide request, we have to check the response to to see which version of the API we received
/// (For example, it's possible to make a request for v=4 to an outdated self-hosted instance and receive
/// a v=3 response).
/// </summary>
internal record DecideApiResult
{
    /// <summary>
    /// The feature flags that are enabled for the user.
    /// </summary>
    public IReadOnlyDictionary<string, StringOrValue<bool>>? FeatureFlags { get; init; }

    /// <summary>
    /// The payloads of the feature flags.
    /// </summary>
    public IReadOnlyDictionary<string, string>? FeatureFlagPayloads { get; init; }

    /// <summary>
    /// Details about the active feature flags.
    /// </summary>
    public IReadOnlyDictionary<string, FeatureFlagResult>? Flags { get; init; }

    /// <summary>
    /// Whether there were errors while computing the feature flags.
    /// </summary>
    public bool ErrorsWhileComputingFlags { get; init; }

    /// <summary>
    /// The name of the features that are limited by quota.
    /// </summary>
    public IReadOnlyList<string>? QuotaLimited { get; init; }

    /// <summary>
    /// The request Id of the API request.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// The timestamp when the feature flags were evaluated (milliseconds since Unix epoch).
    /// </summary>
    public long? EvaluatedAt { get; init; }
}

// This is a transformation of the DecideApiResult to a more usable form.
public record FlagsResult
{
    /// <summary>
    /// A dictionary of feature flags returned by the API.
    /// </summary>
    public IReadOnlyDictionary<string, FeatureFlag> Flags { get; init; } = new Dictionary<string, FeatureFlag>();

    /// <summary>
    /// Whether there were errors while computing the feature flags.
    /// </summary>
    public bool ErrorsWhileComputingFlags { get; init; }

    /// <summary>
    /// The request Id of the API request.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// The timestamp when the feature flags were evaluated (milliseconds since Unix epoch).
    /// </summary>
    public long? EvaluatedAt { get; init; }

    /// <summary>
    /// The list of feature flags that are limited by quota.
    /// </summary>
    public IReadOnlyList<string> QuotaLimited { get; init; } = [];
}

/// <summary>
/// A flag result with more metadata about the flag.
/// </summary>
internal record FeatureFlagWithMetadata : FeatureFlag
{
    /// <summary>
    /// The database id of the flag.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// The version of the flag.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// The reason the flag evaluated as it did.
    /// </summary>
    public required string Reason { get; init; }
}

internal static class DecideResultsExtensions
{
    public static FlagsResult ToFlagsResult(this DecideApiResult? results)
    {
        if (results is null)
        {
            return new FlagsResult();
        }

        var normalized = results.NormalizeResult();

        var failedKeys = normalized.Flags is { } flags
            ? new HashSet<string>(flags.Where(kvp => kvp.Value.Failed == true).Select(kvp => kvp.Key))
            : new HashSet<string>();

        return new FlagsResult
        {
            Flags = normalized.FeatureFlags is { } featureFlags
                ? featureFlags
                    .Where(kvp => !failedKeys.Contains(kvp.Key))
                    .ToReadOnlyDictionary(
                        kvp => kvp.Key,
                        kvp => FeatureFlag.CreateFromDecide(kvp.Key, kvp.Value, normalized))
                : new Dictionary<string, FeatureFlag>(),
            ErrorsWhileComputingFlags = results.ErrorsWhileComputingFlags,
            RequestId = results.RequestId,
            EvaluatedAt = results.EvaluatedAt,
            QuotaLimited = results.QuotaLimited ?? []
        };
    }
}