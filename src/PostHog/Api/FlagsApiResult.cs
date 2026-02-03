using PostHog.Features;
using PostHog.Json;
using PostHog.Library;

namespace PostHog.Api;

/// <summary>
/// The API result for the <c>/flags</c> endpoint. Some properties are omitted because they are not necessary
/// for server-side scenarios.
/// </summary>
internal record FlagsApiResult
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

// This is a transformation of the FlagsApiResult to a more usable form.
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

internal static class FlagsApiResultExtensions
{
    public static FlagsResult ToFlagsResult(this FlagsApiResult? results)
    {
        if (results is null)
        {
            return new FlagsResult();
        }

        var normalized = results.NormalizeResult();

        return new FlagsResult
        {
            Flags = normalized.FeatureFlags is { } featureFlags
                ? featureFlags.ToReadOnlyDictionary(
                    kvp => kvp.Key,
                    kvp => FeatureFlag.CreateFromFlagsApi(kvp.Key, kvp.Value, normalized))
                : new Dictionary<string, FeatureFlag>(),
            ErrorsWhileComputingFlags = results.ErrorsWhileComputingFlags,
            RequestId = results.RequestId,
            EvaluatedAt = results.EvaluatedAt,
            QuotaLimited = results.QuotaLimited ?? []
        };
    }
}