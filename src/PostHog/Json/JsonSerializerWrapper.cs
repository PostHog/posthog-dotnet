using System.Text;
using System.Text.Json;

namespace PostHog.Json;

internal class JsonSerializerWrapper
{
  private readonly PostHogSerializer serializer;

  public JsonSerializerWrapper(PostHogSerializer serializer)
  {
    this.serializer = serializer;
  }

  public async Task<string> SerializeToCamelCaseJsonStringAsync<T>(T obj)
  {
    if (obj == null)
    {
      throw new ArgumentNullException(nameof(obj));
    }

    return await serializer.SerializeAsync(obj);
  }

  public async Task<T> DeserializeFromCamelCaseJsonStringAsync<T>(string json)
  {
    return await serializer.DeserializeAsync<T>(json);
  }

  public async Task<T?> DeserializeFromCamelCaseJsonAsync<T>(
    Stream jsonStream,
    CancellationToken cancellationToken = default)
  {
    using var memoryStream = new MemoryStream();
    await jsonStream.CopyToAsync(memoryStream, cancellationToken);
    memoryStream.Position = 0;
    var json = Encoding.UTF8.GetString(memoryStream.ToArray());
    return await serializer.DeserializeAsync<T>(json, cancellationToken);
  }

  internal static readonly JsonSerializerOptions Options = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    Converters =
        {
            new ReadOnlyCollectionJsonConverterFactory(),
            new ReadOnlyDictionaryJsonConverterFactory()
        }
  };
}