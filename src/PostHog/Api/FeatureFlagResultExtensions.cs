using PostHog.Json;
using PostHog.Library;

namespace PostHog.Api;

internal static class FlagsApiResultNormalizer
{
    /// <summary>
    /// Returns a new <see cref="FlagsApiResult"/> with all the feature flag collections populated. If the response
    /// uses the <see cref="FlagsApiResult.Flags"/> property, we populate <see cref="FlagsApiResult.FeatureFlags"/>
    /// and <see cref="FlagsApiResult.FeatureFlagPayloads"/>. If the response uses <see cref="FlagsApiResult.FeatureFlags"/>,
    /// we populate <see cref="FlagsApiResult.Flags"/> as best as we can.
    /// </summary>
    public static FlagsApiResult NormalizeResult(this FlagsApiResult result)
    {
        return result switch
        {
            { Flags: { } flags } => result with
            {
                FeatureFlags = flags.ToReadOnlyDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToValue()),

                FeatureFlagPayloads = flags
                    .Where(kvp => kvp.Value.Metadata.Payload is not null)
                    .ToReadOnlyDictionary(kvp => kvp.Key, kvp => kvp.Value.Metadata.Payload!)
            },
            { FeatureFlags: { } featureFlags } => result with
            {
                Flags = featureFlags.ToReadOnlyDictionary(
                    kvp => kvp.Key,
                    kvp => FeatureFlagResultExtensions.FromLegacy(
                        kvp.Key,
                        kvp.Value,
                        result.FeatureFlagPayloads?.GetValueOrDefault(kvp.Key)))
            },
            _ => new FlagsApiResult()
        };
    }
}

internal static class FeatureFlagResultExtensions
{
    internal static FeatureFlagResult FromLegacy(
        string key,
        StringOrValue<bool> value,
        string? payload)
    {
        return new FeatureFlagResult
        {
            Key = key,
            Enabled = value.Value,
            Variant = value.StringValue,
            Reason = new EvaluationReason(),
            Metadata = new FeatureFlagMetadata
            {
                Payload = payload,
            }
        };
    }

    internal static StringOrValue<bool> ToValue(this FeatureFlagResult result) =>
        result.Variant is { } variant
            ? new StringOrValue<bool>(variant)
            : new StringOrValue<bool>(result.Enabled);
}