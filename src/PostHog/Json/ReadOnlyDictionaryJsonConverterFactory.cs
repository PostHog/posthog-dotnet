using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostHog.Json;

internal class ReadOnlyDictionaryJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        var genericTypeDefinition = typeToConvert.GetGenericTypeDefinition();
        return genericTypeDefinition == typeof(IReadOnlyDictionary<,>)
               || genericTypeDefinition == typeof(ReadOnlyDictionary<,>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var keyType = typeToConvert.GetGenericArguments()[0];
        var valueType = typeToConvert.GetGenericArguments()[1];
        var converterType = typeof(ReadonlyDictionaryJsonConverter<,>).MakeGenericType(keyType, valueType);

        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}

/// <summary>
/// Converts JSON objects to and from read-only dictionaries.
/// </summary>
/// <typeparam name="TKey">The dictionary key type.</typeparam>
/// <typeparam name="TValue">The dictionary value type.</typeparam>
public class ReadonlyDictionaryJsonConverter<TKey, TValue> : JsonConverter<IReadOnlyDictionary<TKey, TValue>> where TKey : notnull
{
    /// <inheritdoc />
    public override IReadOnlyDictionary<TKey, TValue>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dictionary = JsonSerializer.Deserialize<Dictionary<TKey, TValue>>(ref reader, options);
        return dictionary == null ? null : new ReadOnlyDictionary<TKey, TValue>(dictionary);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<TKey, TValue> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}