using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using PostHog.Api;

namespace PostHog.Json;

internal sealed class ComparisonOperatorJsonConverter : JsonConverter<ComparisonOperator>
{
    private static readonly Dictionary<string, ComparisonOperator> StringToEnum = new();
    private static readonly Dictionary<ComparisonOperator, string> EnumToString = new();

    static ComparisonOperatorJsonConverter()
    {
        var enumType = typeof(ComparisonOperator);
        var enumValues = (ComparisonOperator[])Enum.GetValues(enumType);

        foreach (var value in enumValues)
        {
            var memberName = value.ToString();
            var fieldInfo = enumType.GetField(memberName);
            var jsonPropertyName = fieldInfo?.GetCustomAttribute<JsonPropertyNameAttribute>();

            var jsonName = jsonPropertyName?.Name ?? memberName;

            StringToEnum[jsonName] = value;
            EnumToString[value] = jsonName;
        }
    }

    public override ComparisonOperator Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string value for ComparisonOperator, got {reader.TokenType}");
        }

        var stringValue = reader.GetString();
        if (stringValue == null)
        {
            throw new JsonException("ComparisonOperator cannot be null");
        }

        if (StringToEnum.TryGetValue(stringValue, out var enumValue))
        {
            return enumValue;
        }

        throw new JsonException($"Unknown ComparisonOperator value: {stringValue}");
    }

    public override void Write(Utf8JsonWriter writer, ComparisonOperator value, JsonSerializerOptions options)
    {
        if (EnumToString.TryGetValue(value, out var stringValue))
        {
            writer.WriteStringValue(stringValue);
        }
        else
        {
            throw new JsonException($"Unknown ComparisonOperator value: {value}");
        }
    }
}