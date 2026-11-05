using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using PostHog.Json;

namespace ReadOnlyDictionaryJsonConverterTests;

public class TheWriteMethod
{
    [Fact]
    public void AppliesTheDictionaryKeyPolicyToStringKeys()
    {
        var options = new JsonSerializerOptions
        {
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new ReadOnlyDictionaryJsonConverterFactory() },
        };
        IReadOnlyDictionary<string, int> value = new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int> { ["FirstName"] = 1 });

        Assert.Equal("""{"firstName":1}""", JsonSerializer.Serialize(value, options));
    }

    [Fact]
    public void WritesKeysWithACustomKeyConverterEvenWithAKeyPolicy()
    {
        var options = new JsonSerializerOptions
        {
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new ReadOnlyDictionaryJsonConverterFactory(), new UpperCaseKeyConverter() },
        };
        IReadOnlyDictionary<string, int> value = new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int> { ["FirstName"] = 1 });

        Assert.Equal("""{"FIRSTNAME":1}""", JsonSerializer.Serialize(value, options));
    }

    sealed class UpperCaseKeyConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WritePropertyName(value.ToUpperInvariant());
    }
}
