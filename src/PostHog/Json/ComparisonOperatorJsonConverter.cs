using System.Text.Json;
using System.Text.Json.Serialization;
using PostHog.Api;

namespace PostHog.Json;

internal sealed class ComparisonOperatorJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(ComparisonOperator) ||
               typeToConvert == typeof(ComparisonOperator?);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(ComparisonOperator))
        {
            return new ComparisonOperatorValueConverter();
        }

        if (typeToConvert == typeof(ComparisonOperator?))
        {
            return new NullableComparisonOperatorValueConverter();
        }

        throw new ArgumentException($"Cannot convert type {typeToConvert}");
    }
}

internal sealed class ComparisonOperatorValueConverter : JsonConverter<ComparisonOperator>
{
    private static readonly Dictionary<string, ComparisonOperator> StringToEnum = new()
    {
        ["in"] = ComparisonOperator.In,
        ["exact"] = ComparisonOperator.Exact,
        ["is_not"] = ComparisonOperator.IsNot,
        ["is_set"] = ComparisonOperator.IsSet,
        ["is_not_set"] = ComparisonOperator.IsNotSet,
        ["gt"] = ComparisonOperator.GreaterThan,
        ["lt"] = ComparisonOperator.LessThan,
        ["gte"] = ComparisonOperator.GreaterThanOrEquals,
        ["lte"] = ComparisonOperator.LessThanOrEquals,
        ["icontains"] = ComparisonOperator.ContainsIgnoreCase,
        ["not_icontains"] = ComparisonOperator.DoesNotContainIgnoreCase,
        ["regex"] = ComparisonOperator.Regex,
        ["not_regex"] = ComparisonOperator.NotRegex,
        ["is_date_before"] = ComparisonOperator.IsDateBefore,
        ["is_date_after"] = ComparisonOperator.IsDateAfter
    };

    private static readonly Dictionary<ComparisonOperator, string> EnumToString = new()
    {
        [ComparisonOperator.In] = "in",
        [ComparisonOperator.Exact] = "exact",
        [ComparisonOperator.IsNot] = "is_not",
        [ComparisonOperator.IsSet] = "is_set",
        [ComparisonOperator.IsNotSet] = "is_not_set",
        [ComparisonOperator.GreaterThan] = "gt",
        [ComparisonOperator.LessThan] = "lt",
        [ComparisonOperator.GreaterThanOrEquals] = "gte",
        [ComparisonOperator.LessThanOrEquals] = "lte",
        [ComparisonOperator.ContainsIgnoreCase] = "icontains",
        [ComparisonOperator.DoesNotContainIgnoreCase] = "not_icontains",
        [ComparisonOperator.Regex] = "regex",
        [ComparisonOperator.NotRegex] = "not_regex",
        [ComparisonOperator.IsDateBefore] = "is_date_before",
        [ComparisonOperator.IsDateAfter] = "is_date_after"
    };

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

internal sealed class NullableComparisonOperatorValueConverter : JsonConverter<ComparisonOperator?>
{
    private readonly ComparisonOperatorValueConverter _baseConverter = new();

    public override ComparisonOperator? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        return _baseConverter.Read(ref reader, typeof(ComparisonOperator), options);
    }

    public override void Write(Utf8JsonWriter writer, ComparisonOperator? value, JsonSerializerOptions options)
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