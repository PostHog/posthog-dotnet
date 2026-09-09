using System.Text.Json;
using System.Text.Json.Serialization;
using PostHog.Api;

namespace PostHog.Json;

// Only event properties are normalized; generic JSON for flag requests and caches is unchanged.
internal sealed class CapturedEventJsonConverter : JsonConverter<CapturedEvent>
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1869:Cache and reuse JsonSerializerOptions instances", Justification = "Reading must preserve the caller's options while bypassing this write-boundary converter.")]
    public override CapturedEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var readOptions = new JsonSerializerOptions(options);
        readOptions.Converters.Remove(this);
        return JsonSerializer.Deserialize<CapturedEvent>(ref reader, readOptions);
    }

    public override void Write(Utf8JsonWriter writer, CapturedEvent value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("uuid", value.Uuid);
        writer.WriteString("event", value.EventName);
        writer.WriteString("distinct_id", value.DistinctId);
        writer.WritePropertyName("properties");
        JsonSerializer.Serialize(writer, NormalizeProperties(value.Properties, value.EventName, options), options);
        writer.WritePropertyName("timestamp");
        JsonSerializer.Serialize(writer, value.Timestamp, options);
        writer.WriteEndObject();
    }

    internal static JsonElement NormalizeProperties(object properties, string? eventName, JsonSerializerOptions options)
    {
        // Serialize first so POCOs, DOM values and custom converters follow their existing JSON contracts.
        // Enumerating JSON tokens preserves ordered, case-distinct and duplicate property names.
        var element = JsonSerializer.SerializeToElement(properties, options);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = options.Encoder, MaxDepth = options.MaxDepth }))
        {
            WriteWithoutNullMembers(writer, element, preserveExceptionMetadata: eventName == "$exception");
        }
        stream.Position = 0;
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = options.MaxDepth });
        return document.RootElement.Clone();
    }

    static void WriteWithoutNullMembers(Utf8JsonWriter writer, JsonElement element, bool preserveExceptionMetadata = false)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject())
            {
                // Exception stack frames intentionally have nullable typed fields.
                if (preserveExceptionMetadata && property.NameEquals("$exception_list"))
                {
                    property.WriteTo(writer);
                }
                else if (property.Value.ValueKind != JsonValueKind.Null)
                {
                    writer.WritePropertyName(property.Name);
                    WriteWithoutNullMembers(writer, property.Value);
                }
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
            {
                WriteWithoutNullMembers(writer, item);
            }
            writer.WriteEndArray();
        }
        else
        {
            element.WriteTo(writer);
        }
    }
}
