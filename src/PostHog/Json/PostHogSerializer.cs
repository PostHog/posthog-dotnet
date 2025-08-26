namespace PostHog.Json;

/// <summary>
/// Abstract base class for JSON serialization operations used by the PostHog client.
/// </summary>
public abstract class PostHogSerializer
{
  /// <summary>
  /// Serializes an object to a JSON string.
  /// </summary>
  /// <param name="obj">The object to serialize.</param>
  /// <returns>The JSON string representation of the object.</returns>
  public abstract string Serialize(object obj);

  /// <summary>
  /// Deserializes a JSON string to an object of the specified type.
  /// </summary>
  /// <typeparam name="T">The type to deserialize to.</typeparam>
  /// <param name="json">The JSON string to deserialize.</param>
  /// <returns>The deserialized object.</returns>
  public abstract T Deserialize<T>(string json);

  /// <summary>
  /// Serializes an object to a JSON string asynchronously.
  /// </summary>
  /// <param name="obj">The object to serialize.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The JSON string representation of the object.</returns>
  public virtual Task<string> SerializeAsync(object obj, CancellationToken cancellationToken = default)
  {
    return Task.FromResult(Serialize(obj));
  }

  /// <summary>
  /// Deserializes a JSON string to an object of the specified type asynchronously.
  /// </summary>
  /// <typeparam name="T">The type to deserialize to.</typeparam>
  /// <param name="json">The JSON string to deserialize.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>The deserialized object.</returns>
  public virtual Task<T> DeserializeAsync<T>(string json, CancellationToken cancellationToken = default)
  {
    return Task.FromResult(Deserialize<T>(json));
  }
}