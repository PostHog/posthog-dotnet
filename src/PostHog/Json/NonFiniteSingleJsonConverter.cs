using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostHog.Json;

internal sealed class NonFiniteSingleJsonConverter : JsonConverter<float>
{
    public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Number
            ? reader.GetSingle()
            : JsonSerializer.Deserialize<float>(ref reader, NumberHandlingJsonTypeInfoResolver.GetBuiltInOptions(options.NumberHandling));

    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            writer.WriteNullValue();
        }
        else if ((options.NumberHandling & JsonNumberHandling.WriteAsString) != 0)
        {
            JsonSerializer.Serialize(writer, value, NumberHandlingJsonTypeInfoResolver.GetBuiltInOptions(options.NumberHandling));
        }
        else
        {
            writer.WriteNumberValue(value);
        }
    }
}
