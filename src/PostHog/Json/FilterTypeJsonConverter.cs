using System.Text.Json;
using System.Text.Json.Serialization;
using PostHog.Api;

namespace PostHog.Json;

internal sealed class FilterTypeJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(FilterType) ||
               typeToConvert == typeof(FilterType?);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(FilterType))
        {
            return new FilterTypeValueConverter();
        }

        if (typeToConvert == typeof(FilterType?))
        {
            return new NullableFilterTypeValueConverter();
        }

        throw new ArgumentException($"Cannot convert type {typeToConvert}");
    }
}

internal sealed class FilterTypeValueConverter : JsonConverter<FilterType>
{
    private static readonly Dictionary<string, FilterType> StringToEnum = new()
    {
        ["person"] = FilterType.Person,
        ["group"] = FilterType.Group,
        ["cohort"] = FilterType.Cohort,
        ["OR"] = FilterType.Or,
        ["AND"] = FilterType.And
    };

    private static readonly Dictionary<FilterType, string> EnumToString = new()
    {
        [FilterType.Person] = "person",
        [FilterType.Group] = "group",
        [FilterType.Cohort] = "cohort",
        [FilterType.Or] = "OR",
        [FilterType.And] = "AND"
    };

    public override FilterType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string value for FilterType, got {reader.TokenType}");
        }

        var stringValue = reader.GetString();
        if (stringValue == null)
        {
            throw new JsonException("FilterType string value cannot be null");
        }

        if (StringToEnum.TryGetValue(stringValue, out var enumValue))
        {
            return enumValue;
        }

        throw new JsonException($"Unknown FilterType value: {stringValue}");
    }

    public override void Write(Utf8JsonWriter writer, FilterType value, JsonSerializerOptions options)
    {
        if (EnumToString.TryGetValue(value, out var stringValue))
        {
            writer.WriteStringValue(stringValue);
        }
        else
        {
            throw new JsonException($"Unknown FilterType value: {value}");
        }
    }
}

internal sealed class NullableFilterTypeValueConverter : JsonConverter<FilterType?>
{
    private readonly FilterTypeValueConverter _baseConverter = new();

    public override FilterType? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        return _baseConverter.Read(ref reader, typeof(FilterType), options);
    }

    public override void Write(Utf8JsonWriter writer, FilterType? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
        }
        else
        {
            _baseConverter.Write(writer, value.Value, options);
        }
    }
}