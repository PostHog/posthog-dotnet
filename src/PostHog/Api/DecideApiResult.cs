using PostHog.Features;
using PostHog.Json;
using PostHog.Library;

namespace PostHog.Api;

/// <summary>
/// The API Result for the <c>/decide</c> endpoint. Some properties are omitted because
/// they are not necessary for server-side scenarios.
/// </summary>
/// <param name="FeatureFlags">The feature flags that are enabled for the user.</param>
/// <param name="ErrorsWhileComputingFlags">Whether there were errors while computing the feature flags.</param>
/// <param name="FeatureFlagPayloads">The payloads of the feature flags.</param>
/// <param name="QuotaLimited">The list of feature flags that are limited by quota.</param>
/// <param name="RequestId">The request Id of the API request.</param>
internal record DecideApiResult(
    IReadOnlyDictionary<string, StringOrValue<bool>>? FeatureFlags = null,
    bool ErrorsWhileComputingFlags = false,
    IReadOnlyDictionary<string, string>? FeatureFlagPayloads = null,
    IReadOnlyList<string>? QuotaLimited = null,
    string? RequestId = null);

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
    /// The list of feature flags that are limited by quota.
    /// </summary>
    public IReadOnlyList<string> QuotaLimited { get; init; } = [];
}

internal static class DecideResultsExtensions
{
    public static FlagsResult ToFlagsResult(this DecideApiResult? results)
    {
        if (results is null)
        {
            return new FlagsResult();
        }

        var flags = results.FeatureFlags is not null
            ? results.FeatureFlags.ToReadOnlyDictionary(
                kvp => kvp.Key,
                kvp => FeatureFlag.CreateFromDecide(kvp.Key, kvp.Value, results))
            : new Dictionary<string, FeatureFlag>();

        return new FlagsResult
        {
            Flags = flags,
            ErrorsWhileComputingFlags = results.ErrorsWhileComputingFlags,
            RequestId = results.RequestId,
            QuotaLimited = results.QuotaLimited ?? []
        };
    }
}