using System.Text.Json;
using System.Text.Json.Serialization;
using PostHog.Library;

namespace PostHog.Json;

using static Ensure;

internal sealed class PropertyFilterValueJsonConverter : JsonConverter<PropertyFilterValue>
{
    public override PropertyFilterValue? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var filterElement = JsonDocument.ParseValue(ref reader).RootElement;
        return PropertyFilterValue.Create(filterElement);
    }

    public override void Write(Utf8JsonWriter writer, PropertyFilterValue value, JsonSerializerOptions options)
    {
        writer = NotNull(writer);

        switch (value)
        {
            case { StringValue: { } stringValue }:
                writer.WriteStringValue(stringValue);
                break;
            case { CohortId: { } cohortId }:
                writer.WriteNumberValue(cohortId);
                break;
            case { ListOfStrings: { } stringArray }:
                {
                    // Begin writing the JSON array
                    writer.WriteStartArray();

                    // Iterate through the list and write each string value
                    foreach (var item in stringArray)
                    {
                        writer.WriteStringValue(item);
                    }

                    // End the JSON array
                    writer.WriteEndArray();
                    break;
                }
            case null:
                {
                    writer.WriteNullValue();
                    break;
                }
        }
    }
}