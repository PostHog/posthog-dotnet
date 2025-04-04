using PostHog.Json;
using PostHog.Library;

namespace PostHog.Api;

internal static class DecideApiResultExtensions
{
    /// <summary>
    /// Returns a new DecideApiResult with all the feature flag collections populated. If we get a v4 response,
    /// we populate <see cref="DecideApiResult.FeatureFlags"/> and <see cref="DecideApiResult.FeatureFlagPayloads"/>.
    /// If we get a v3 response, we populate <see cref="DecideApiResult.Flags"/> as best as we can.
    /// </summary>
    /// <returns></returns>
    public static DecideApiResult NormalizeResult(this DecideApiResult result)
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
                    kvp => FeatureFlagResultExtensions.FromV3(
                        kvp.Key,
                        kvp.Value,
                        result.FeatureFlagPayloads?.GetValueOrDefault(kvp.Key)))
            },
            _ => new DecideApiResult()
        };
    }
}

internal static class FeatureFlagResultExtensions
{
    internal static FeatureFlagResult FromV3(
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