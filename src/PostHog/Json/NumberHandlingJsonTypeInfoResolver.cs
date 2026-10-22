using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PostHog.Json;

internal sealed class NumberHandlingJsonTypeInfoResolver(JsonNumberHandling? inheritedNumberHandling = null, Type? memberType = null) : DefaultJsonTypeInfoResolver
{
    readonly JsonNumberHandling? _inheritedNumberHandling = inheritedNumberHandling;
    static readonly JsonSerializerOptions[] BuiltInOptions = Enumerable.Range(0, 8)
        .Select(value => new JsonSerializerOptions { NumberHandling = (JsonNumberHandling)value }).ToArray();

    internal static JsonSerializerOptions GetBuiltInOptions(JsonNumberHandling handling) => BuiltInOptions[(int)handling];

    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = base.GetTypeInfo(type, options);
        if (type == memberType && typeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary)
        {
            typeInfo.NumberHandling = options.NumberHandling;
        }
        if (typeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
            && IsAffected(typeInfo) && typeInfo.NumberHandling is { } typeHandling && typeHandling != options.NumberHandling)
        {
            return (JsonTypeInfo)typeof(NumberHandlingJsonTypeInfoResolver)
                .GetMethod(nameof(CreateTypeInfo), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(type).Invoke(null, [options, WithNumberHandling(options, typeHandling)])!;
        }

        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return typeInfo;
        }

        // A member's number handling applies to collection elements, not members of nested models.
        var usesInheritedHandling = typeInfo.NumberHandling is null;
        typeInfo.NumberHandling ??= _inheritedNumberHandling;
        foreach (var property in typeInfo.Properties)
        {
            if (property.CustomConverter is not null)
            {
                continue;
            }
            var propertyTypeInfo = base.GetTypeInfo(property.PropertyType, options);
            if (usesInheritedHandling && IsAffected(propertyTypeInfo))
            {
                property.NumberHandling ??= propertyTypeInfo.NumberHandling;
            }
            var handling = property.NumberHandling ?? typeInfo.NumberHandling ?? propertyTypeInfo.NumberHandling ?? options.NumberHandling;
            if (!IsAffected(propertyTypeInfo) || (handling == options.NumberHandling
                && (propertyTypeInfo.NumberHandling is null || propertyTypeInfo.NumberHandling == handling)))
            {
                continue;
            }

            var converterType = typeof(NumberHandlingJsonConverter<>).MakeGenericType(property.PropertyType);
            property.CustomConverter = (JsonConverter)Activator.CreateInstance(converterType, WithNumberHandling(options, handling, property.PropertyType))!;
            property.NumberHandling = null;
        }
        return typeInfo;
    }

    static bool IsAffected(JsonTypeInfo typeInfo)
    {
        var type = typeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary ? typeInfo.ElementType! : typeInfo.Type;
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(double) || type == typeof(float) || type == typeof(object);
    }

    static JsonSerializerOptions WithNumberHandling(JsonSerializerOptions options, JsonNumberHandling handling, Type? memberType = null)
        => new(options)
        {
            NumberHandling = handling,
            TypeInfoResolver = new NumberHandlingJsonTypeInfoResolver(
                (options.TypeInfoResolver as NumberHandlingJsonTypeInfoResolver)?._inheritedNumberHandling ?? options.NumberHandling, memberType)
        };

    static JsonTypeInfo<T> CreateTypeInfo<T>(JsonSerializerOptions options, JsonSerializerOptions numberOptions)
        => JsonMetadataServices.CreateValueInfo<T>(options, new NumberHandlingJsonConverter<T>(numberOptions));
}

internal sealed class NumberHandlingJsonConverter<T>(JsonSerializerOptions numberOptions) : JsonConverter<T>
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<T>(ref reader, numberOptions);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, numberOptions);
}
