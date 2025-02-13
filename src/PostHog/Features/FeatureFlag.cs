using System.Text.Json;
using System.Text.Json.Nodes;
using PostHog.Api;
using PostHog.Json;
using static PostHog.Library.Ensure;

namespace PostHog.Features;

/// <summary>
/// Represents a feature flag.
/// </summary>
public record FeatureFlag
{
    /// <summary>
    /// The key of the feature flag.
    /// </summary>
    public required string Key { get; init; }

    public JsonDocument? Payload { get; init; }

    public string? VariantKey { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Creates a <see cref="FeatureFlag"/> instance as a result of the /decide endpoint response. Since payloads are
    /// already calculated, we can look them up by the feature key.
    /// </summary>
    /// <param name="key">The feature flag key.</param>
    /// <param name="value">The value of the flag.</param>
    /// <param name="apiResult">The Decide Api result.</param>
    internal static FeatureFlag CreateFromDecide(
        string key,
        StringOrValue<bool> value,
        DecideApiResult apiResult)
    {
        var payload = NotNull(apiResult).FeatureFlagPayloads?.GetValueOrDefault(key);
        return new FeatureFlag
        {
            Key = key,
            IsEnabled = value.IsString ? value.StringValue is not null : value.Value,
            VariantKey = value.StringValue,
            Payload = payload is null ? null : JsonDocument.Parse(payload)
        };
    }

    /// <summary>
    /// Creates a <see cref="FeatureFlag"/> instance as part of local evaluation. It makes sure to look up the
    /// payload based on the value of the feature flag.
    /// </summary>
    /// <param name="key">The feature flag key.</param>
    /// <param name="value">The value of the flag.</param>
    /// <param name="localFeatureFlag">The feature flag definition.</param>
    internal static FeatureFlag CreateFromLocalEvaluation(
        string key,
        StringOrValue<bool> value,
        LocalFeatureFlag localFeatureFlag)
    {
#pragma warning disable CA1308
        var payloadKey = value.StringValue ?? value.Value.ToString().ToLowerInvariant();
#pragma warning restore CA1308
        var payloadJsonString = NotNull(localFeatureFlag).Filters?.Payloads?.GetValueOrDefault(payloadKey);
        return new FeatureFlag
        {
            Key = key,
            IsEnabled = value.IsString ? value.StringValue is not null : value.Value,
            VariantKey = value.StringValue,
            Payload = payloadJsonString is null ? null : JsonDocument.Parse(payloadJsonString)
        };
    }

    public virtual bool Equals(FeatureFlag? other)
    {
        return other is not null
               && Key == other.Key
               && IsEnabled == other.IsEnabled
               && VariantKey == other.VariantKey
               && JsonEqual(Payload,  other.Payload);
    }

    public override int GetHashCode() => HashCode.Combine(Key, IsEnabled, VariantKey, Payload);

    static bool JsonEqual(JsonDocument? source, JsonDocument? comparand) =>
        JsonNode.DeepEquals(ToJsonNode(source), ToJsonNode(comparand));

    static JsonNode? ToJsonNode(JsonDocument? jsonDocument) => jsonDocument is null
        ? null
        : JsonNode.Parse(jsonDocument.RootElement.GetRawText());

    /// <summary>
    /// Implicit cast to nullable boolean.
    /// </summary>
    /// <param name="flag">The <see cref="FeatureFlag"/>.</param>
    /// <returns><c>true</c> if this feature flag is enabled, <c>false</c> if it is not, and <c>null</c> if it can't be determined.</returns>
#pragma warning disable CA2225
    public static implicit operator bool(FeatureFlag? flag) => flag is { IsEnabled: true };
#pragma warning restore CA2225

    /// <summary>
    /// Implicit cast to string. This returns the variant key if there is one, otherwise "true" or "false" depending
    /// on the result of the flag evaluation.
    /// </summary>
    /// <param name="flag">The <see cref="FeatureFlag"/>.</param>
    /// <returns>The variant key, if this flag is enabled and has a variant key, otherwise the IsEnabled value as a string.</returns>
    public static implicit operator string(FeatureFlag? flag) => flag?.VariantKey ?? ((bool)flag).ToString();
}