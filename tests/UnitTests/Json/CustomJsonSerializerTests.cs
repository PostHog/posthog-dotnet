using PostHog;
using PostHog.Json;

namespace CustomJsonSerializerTests;

public class CustomJsonSerializerTests
{
  [Fact]
  public void PostHogOptions_DefaultSerializer_IsSystemTextJsonSerializer()
  {
    // Arrange & Act
    var options = new PostHogOptions();

    // Assert
    Assert.IsType<SystemTextJsonSerializer>(options.JsonSerializer);
  }

  [Fact]
  public void PostHogOptions_CanSetCustomSerializer()
  {
    // Arrange
    var customSerializer = new CustomTestSerializer();
    var options = new PostHogOptions();

    // Act
    options.JsonSerializer = customSerializer;

    // Assert
    Assert.Same(customSerializer, options.JsonSerializer);
    Assert.IsType<CustomTestSerializer>(options.JsonSerializer);
  }

  [Fact]
  public async Task SystemTextJsonSerializer_SerializeAndDeserialize_WorksCorrectly()
  {
    // Arrange
    var serializer = new SystemTextJsonSerializer();
    var testObject = new TestClass { Name = "Test", Value = 42 };

    // Act
    var json = await serializer.SerializeAsync(testObject);
    var deserialized = await serializer.DeserializeAsync<TestClass>(json);

    // Assert
    Assert.Equal("Test", deserialized.Name);
    Assert.Equal(42, deserialized.Value);
  }

  [Fact]
  public async Task CustomSerializer_SerializeAndDeserialize_WorksCorrectly()
  {
    // Arrange
    var serializer = new CustomTestSerializer();
    var testObject = new TestClass { Name = "Test", Value = 42 };

    // Act
    var json = await serializer.SerializeAsync(testObject);
    var deserialized = await serializer.DeserializeAsync<TestClass>(json);

    // Assert
    Assert.Equal("Test", deserialized.Name);
    Assert.Equal(42, deserialized.Value);
    Assert.True(serializer.SerializeCalled);
    Assert.True(serializer.DeserializeCalled);
  }

  private class TestClass
  {
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
  }

  private sealed class CustomTestSerializer : PostHogSerializer
  {
    public bool SerializeCalled { get; private set; }
    public bool DeserializeCalled { get; private set; }

    private readonly SystemTextJsonSerializer _fallback = new();

    public override string Serialize(object obj)
    {
      SerializeCalled = true;
      return _fallback.Serialize(obj);
    }

    public override T Deserialize<T>(string json)
    {
      DeserializeCalled = true;
      return _fallback.Deserialize<T>(json);
    }
  }
}