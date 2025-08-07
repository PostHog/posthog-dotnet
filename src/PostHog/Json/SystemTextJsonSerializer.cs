using System.Text;
using System.Text.Json;

namespace PostHog.Json;

/// <summary>
/// Default JSON serializer implementation using System.Text.Json.
/// </summary>
public class SystemTextJsonSerializer : PostHogSerializer
{
  private static readonly JsonSerializerOptions Options = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    Converters =
        {
            new ReadOnlyCollectionJsonConverterFactory(),
            new ReadOnlyDictionaryJsonConverterFactory()
        }
  };

  /// <inheritdoc />
  public override string Serialize(object obj)
  {
    return JsonSerializer.Serialize(obj, Options);
  }

  /// <inheritdoc />
  public override T Deserialize<T>(string json)
  {
    return JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidOperationException($"Failed to deserialize JSON to type {typeof(T).Name}");
  }

  /// <inheritdoc />
  public override async Task<T> DeserializeAsync<T>(string json, CancellationToken cancellationToken = default)
  {
    using var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    jsonStream.Position = 0;
    return await JsonSerializer.DeserializeAsync<T>(jsonStream, Options, cancellationToken) ?? throw new InvalidOperationException($"Failed to deserialize JSON to type {typeof(T).Name}");
  }
}