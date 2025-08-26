using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostHog.Json;

/// <summary>
/// Generic JSON converter for enums that respects custom JsonStringEnumMemberName attributes.
/// Provides .NET 8 compatibility for custom enum member names.
/// </summary>
/// <typeparam name="TEnum">The enum type to convert.</typeparam>
internal class JsonStringEnumMemberNameJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> StringToEnum = CreateStringToEnumMapping();
    private static readonly Dictionary<TEnum, string> EnumToString = CreateEnumToStringMapping();

    private static Dictionary<string, TEnum> CreateStringToEnumMapping()
    {
        var mapping = new Dictionary<string, TEnum>();

        foreach (var (enumValue, name) in GetEnumFieldMappings())
        {
            mapping[name] = enumValue;
        }

        return mapping;
    }

    private static Dictionary<TEnum, string> CreateEnumToStringMapping()
    {
        var mapping = new Dictionary<TEnum, string>();

        foreach (var (enumValue, name) in GetEnumFieldMappings())
        {
            mapping[enumValue] = name;
        }

        return mapping;
    }

    private static IEnumerable<(TEnum EnumValue, string Name)> GetEnumFieldMappings()
    {
        var enumType = typeof(TEnum);

        foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var enumValue = (TEnum)field.GetValue(null)!;
            var jsonStringEnumMemberName = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>();

            var name = jsonStringEnumMemberName?.Name ?? field.Name;
            yield return (enumValue, name);
        }
    }

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (stringValue != null && StringToEnum.TryGetValue(stringValue, out var enumValue))
            {
                return enumValue;
            }
            throw new JsonException($"Unable to convert \"{stringValue}\" to {typeof(TEnum).Name}.");
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} when parsing {typeof(TEnum).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (EnumToString.TryGetValue(value, out var stringValue))
        {
            writer.WriteStringValue(stringValue);
        }
        else
        {
            throw new JsonException($"Unable to convert {value} to string representation.");
        }
    }
}