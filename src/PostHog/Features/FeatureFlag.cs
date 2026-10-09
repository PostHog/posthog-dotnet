using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// The payload, if any, associated with the feature flag.
    /// </summary>
    public JsonDocument? Payload { get; init; }

    /// <summary>
    /// The variant key selected for this feature flag.
    /// </summary>
    public string? VariantKey { get; init; }

    /// <summary>
    /// Whether this feature flag evaluated to <c>true</c> or <c>false</c>.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Whether this feature flag is linked to an experiment, as reported by the server.
    /// <c>null</c> when the server does not report it (older deployments).
    /// </summary>
    public bool? HasExperiment { get; init; }

    /// <summary>
    /// Creates a <see cref="FeatureFlag"/> instance from the <c>/flags</c> endpoint response. Since payloads are
    /// already calculated, we can look them up by the feature key.
    /// </summary>
    /// <param name="key">The feature flag key.</param>
    /// <param name="value">The value of the flag.</param>
    /// <param name="apiResult">The flags API result.</param>
    /// <param name="logger">The logger used to report a malformed payload.</param>
    internal static FeatureFlag CreateFromFlagsApi(
        string key,
        StringOrValue<bool> value,
        FlagsApiResult apiResult,
        ILogger? logger = null)
    {
        var payload = NotNull(apiResult).FeatureFlagPayloads?.GetValueOrDefault(key);
        var flag = apiResult.Flags?.GetValueOrDefault(key);

        var featureFlag = flag is not null
                          && flag.Metadata is { Id: { } id, Version: { } version }
                          && flag.Reason?.Description is { } reason
            ? new FeatureFlagWithMetadata
            {
                Key = flag.Key,
                Id = id,
                Version = version,
                Reason = reason,
            }
            : new FeatureFlag
            {
                Key = key
            };


        return featureFlag with
        {
            IsEnabled = value.IsString ? value.StringValue is not null : value.Value,
            VariantKey = value.StringValue,
            Payload = ParsePayloadOrNull(payload, key, logger),
            HasExperiment = flag?.Metadata?.HasExperiment
        };
    }

    /// <summary>
    /// Creates a <see cref="FeatureFlag"/> instance as part of local evaluation. It makes sure to look up the
    /// payload based on the value of the feature flag.
    /// </summary>
    /// <param name="key">The feature flag key.</param>
    /// <param name="value">The value of the flag.</param>
    /// <param name="localFeatureFlag">The feature flag definition.</param>
    /// <param name="logger">The logger used to report a malformed payload.</param>
    internal static FeatureFlag CreateFromLocalEvaluation(
        string key,
        StringOrValue<bool> value,
        LocalFeatureFlag localFeatureFlag,
        ILogger? logger = null)
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
            Payload = ParsePayloadOrNull(payloadJsonString, key, logger),
            HasExperiment = localFeatureFlag.HasExperiment
        };
    }

    /// <summary>
    /// Decodes a serialized payload. A malformed, empty, or whitespace-only payload is logged and treated the same
    /// way as a flag with no payload at all, so neither the flag's value nor its healthy siblings are lost.
    /// </summary>
    static JsonDocument? ParsePayloadOrNull(string? serializedPayload, string key, ILogger? logger)
    {
        if (serializedPayload is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(serializedPayload))
        {
            logger?.LogWarnEmptyFeatureFlagPayload(key);
            return null;
        }

        try
        {
            return JsonDocument.Parse(serializedPayload);
        }
        catch (JsonException e)
        {
            logger?.LogWarnMalformedFeatureFlagPayload(e, key);
            return null;
        }
    }

    /// <summary>
    /// Determines whether the specified <see cref="FeatureFlag"/> is equal to the current <see cref="FeatureFlag"/>.
    /// </summary>
    /// <param name="other">The <see cref="FeatureFlag"/> to compare with the current <see cref="FeatureFlag"/>.</param>
    /// <returns><c>true</c> if the specified <see cref="FeatureFlag"/> is equal to the current</returns>
    public virtual bool Equals(FeatureFlag? other) =>
        other is not null
        && Key == other.Key
        && IsEnabled == other.IsEnabled
        && VariantKey == other.VariantKey
        && HasExperiment == other.HasExperiment
        && JsonEqual(Payload, other.Payload);

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current <see cref="FeatureFlag"/>.</returns>
    public override int GetHashCode() => HashCode.Combine(Key, IsEnabled, VariantKey, HasExperiment, Payload);

    static bool JsonEqual(JsonDocument? source, JsonDocument? comparand) =>
        JsonNode.DeepEquals(ToJsonNode(source), ToJsonNode(comparand));

    static JsonNode? ToJsonNode(JsonDocument? jsonDocument) => jsonDocument is null
        ? null
        : JsonNode.Parse(jsonDocument.RootElement.GetRawText());

    /// <summary>
    /// Implicit cast to boolean.
    /// </summary>
    /// <param name="flag">The <see cref="FeatureFlag"/>.</param>
    /// <returns><c>true</c> if this feature flag is non-null and enabled; otherwise <c>false</c>.</returns>
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

internal static partial class FeatureFlagLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "[FEATURE FLAGS] The payload for feature flag {FlagKey} is empty. Treating it as no payload.")]
    public static partial void LogWarnEmptyFeatureFlagPayload(this ILogger logger, string flagKey);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "[FEATURE FLAGS] The payload for feature flag {FlagKey} is not valid JSON. Treating it as no payload.")]
    public static partial void LogWarnMalformedFeatureFlagPayload(this ILogger logger, Exception exception, string flagKey);
}
