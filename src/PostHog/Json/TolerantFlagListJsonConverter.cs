using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using PostHog.Api;

namespace PostHog.Json;

/// <summary>
/// Deserializes the <c>flags</c> array from the <c>/flags/definitions</c> payload one element at a
/// time. A single flag with an unexpected shape (for example a field with the wrong JSON type) is
/// skipped instead of failing the whole payload. This keeps one bad flag from disabling local
/// evaluation for every other flag, which would otherwise force per-call remote evaluation.
/// </summary>
internal sealed class TolerantFlagListJsonConverter : JsonConverter<IReadOnlyList<LocalFeatureFlag>>
{
    public override IReadOnlyList<LocalFeatureFlag> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected the start of an array for feature flag definitions.");
        }

        var flags = new List<LocalFeatureFlag>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            // Capture the element so a malformed flag can be skipped without consuming the rest.
            using var element = JsonDocument.ParseValue(ref reader);
            try
            {
                if (element.RootElement.Deserialize<LocalFeatureFlag>(options) is { } flag)
                {
                    flags.Add(flag);
                }
            }
            catch (JsonException)
            {
                // Drop this flag so the remaining flags still evaluate locally.
            }
        }

        return new ReadOnlyCollection<LocalFeatureFlag>(flags);
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<LocalFeatureFlag> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var flag in value)
        {
            JsonSerializer.Serialize(writer, flag, options);
        }

        writer.WriteEndArray();
    }
}
