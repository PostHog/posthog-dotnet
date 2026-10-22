using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostHog.Json;

internal sealed class NonFiniteSingleJsonConverter : JsonConverter<float>
{
    public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetSingle();

    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value);
        }
    }
}
