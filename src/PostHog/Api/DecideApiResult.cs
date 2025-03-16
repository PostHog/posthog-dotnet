using PostHog.Json;

namespace PostHog.Api;

/// <summary>
/// The API Result for the <c>/decide</c> endpoint. Some properties are omitted because
/// they are not necessary for server-side scenarios.
/// </summary>
/// <param name="FeatureFlags">The feature flags that are enabled for the user.</param>
/// <param name="ErrorsWhileComputingFlags">Whether there were errors while computing the feature flags.</param>
/// <param name="FeatureFlagPayloads">The payloads of the feature flags.</param>
/// <param name="QuotaLimited">The list of feature flags that are limited by quota.</param>
internal record DecideApiResult(
    IReadOnlyDictionary<string, StringOrValue<bool>>? FeatureFlags = null,
    bool ErrorsWhileComputingFlags = false,
    IReadOnlyDictionary<string, string>? FeatureFlagPayloads = null,
    IReadOnlyList<string>? QuotaLimited = null);