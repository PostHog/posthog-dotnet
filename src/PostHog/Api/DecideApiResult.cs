using System.Text.Json.Serialization;
using PostHog.Json;

namespace PostHog.Api;

/// <summary>
/// The API Result for the <c>/decide</c> endpoint. Some properties are ommitted because
/// they are not necessary for server-side scenarios.
/// </summary>
/// <remarks>
///
/// </remarks>
internal record DecideApiResult(
    FeatureFlagsConfig? Config = null,
    bool IsAuthenticated = false,
    IReadOnlyDictionary<string, StringOrValue<bool>>? FeatureFlags = null,
    Analytics? Analytics = null,
    bool DefaultIdentifiedOnly = true,
    bool ErrorsWhileComputingFlags = false,
    IReadOnlyDictionary<string, string>? FeatureFlagPayloads = null);

internal record FeatureFlagsConfig([property: JsonPropertyName("enable_collect_everything")] bool EnableCollectEverything);
internal record Analytics(string Endpoint);
