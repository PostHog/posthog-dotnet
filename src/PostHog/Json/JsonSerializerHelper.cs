using System.Text;
using System.Text.Json;

namespace PostHog.Json;

internal static class JsonSerializerHelper
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new ReadOnlyCollectionJsonConverterFactory(),
            new ReadOnlyDictionaryJsonConverterFactory(),
            new ComparisonOperatorJsonConverter(),
            new FilterTypeJsonConverter()
        }
    };

    static readonly JsonSerializerOptions IndentedOptions = new(Options)
    {
        WriteIndented = true
    };
    public static async Task<string> SerializeToCamelCaseJsonStringAsync<T>(T obj, bool writeIndented = false)
    {
        var stream = await SerializeToCamelCaseJsonStreamAsync(obj, writeIndented);
        stream.Position = 0;
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        return Encoding.UTF8.GetString(memoryStream.ToArray());
    }

    static async Task<Stream> SerializeToCamelCaseJsonStreamAsync<T>(T obj, bool writeIndented = false)
    {
        var options = writeIndented ? IndentedOptions : Options;
        var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, obj, options);
        return stream;
    }

    public static async Task<T?> DeserializeFromCamelCaseJsonStringAsync<T>(string json)
    {
        using var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        jsonStream.Position = 0;
        return await DeserializeFromCamelCaseJsonAsync<T>(jsonStream);
    }

    public static async Task<T?> DeserializeFromCamelCaseJsonAsync<T>(
        Stream jsonStream,
        CancellationToken cancellationToken = default) =>
        await JsonSerializer.DeserializeAsync<T>(jsonStream, Options, cancellationToken);
}

