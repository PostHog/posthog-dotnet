using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PostHog;
using PostHog.Api;
using PostHog.Json;

namespace EventPropertySerializationTests;

public class TheEventSerializationBoundary
{
    static Dictionary<string, object> Graph() => new()
    {
        ["test"] = null!,
        ["nested"] = new Dictionary<string, object> { ["drop"] = null! },
        ["items"] = new object?[] { "1", null, 2, new { Drop = (object?)null }, new object?[] { null } },
        ["empty"] = "",
        ["zero"] = 0,
        ["enabled"] = false,
        ["literal"] = "null",
        ["literalUndefined"] = "undefined",
        ["emptyArray"] = Array.Empty<object>(),
        ["node"] = JsonNode.Parse("{\"drop\":null,\"items\":[null,{\"drop\":null}]}")!,
        ["element"] = JsonSerializer.Deserialize<JsonElement>("{\"drop\":null}"),
        ["jsonNull"] = JsonSerializer.Deserialize<JsonElement>("null"),
        ["Foo"] = 1,
        ["foo"] = 2,
        ["duplicates"] = JsonSerializer.Deserialize<JsonElement>("{\"x\":1,\"x\":null,\"x\":2,\"drop\":null,\"items\":[null,{\"x\":3,\"x\":4,\"drop\":null}],\"precise\":1.2345678901234567890123456789,\"large\":1e400}"),
        ["converted"] = new JsonAwareValue(),
        ["convertedNull"] = new JsonNullValue(),
        ["$set"] = new Dictionary<string, object> { ["drop"] = null! },
        ["$group_set"] = new Dictionary<string, object> { ["drop"] = null! }
    };

    static void AssertClean(JsonElement properties)
    {
        Assert.False(properties.TryGetProperty("test", out _));
        Assert.False(properties.TryGetProperty("missing", out _));
        Assert.False(properties.TryGetProperty("jsonNull", out _));
        Assert.False(properties.TryGetProperty("convertedNull", out _));
        Assert.Equal(new[] { "Foo", "foo" }, properties.EnumerateObject().Where(p => p.Name.Equals("foo", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name));
        Assert.Equal(1, properties.GetProperty("Foo").GetInt32());
        Assert.Equal(2, properties.GetProperty("foo").GetInt32());
        Assert.Equal("{\"x\":1,\"x\":2,\"items\":[null,{\"x\":3,\"x\":4}],\"precise\":1.2345678901234567890123456789,\"large\":1e400}", properties.GetProperty("duplicates").GetRawText());
        foreach (var key in new[] { "nested", "element", "$set", "$group_set", "converted" })
            Assert.Equal("{}", properties.GetProperty(key).GetRawText());
        Assert.Equal("[\"1\",null,2,{},[null]]", properties.GetProperty("items").GetRawText());
        Assert.Equal("{\"items\":[null,{}]}", properties.GetProperty("node").GetRawText());
        Assert.Equal("", properties.GetProperty("empty").GetString());
        Assert.Equal(0, properties.GetProperty("zero").GetInt32());
        Assert.False(properties.GetProperty("enabled").GetBoolean());
        Assert.Equal("null", properties.GetProperty("literal").GetString());
        Assert.Equal("undefined", properties.GetProperty("literalUndefined").GetString());
        Assert.Equal("[]", properties.GetProperty("emptyArray").GetRawText());
    }

    [Fact]
    public async Task CapturedEventCleansJsonAwareValuesWithoutMutatingInputsOrGenericSerialization()
    {
        var graph = Graph();
        var original = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(graph);
        var evt = new CapturedEvent("test", "user", graph, DateTimeOffset.UtcNow);
        using var json = JsonDocument.Parse(await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(evt));
        AssertClean(json.RootElement.GetProperty("properties"));
        Assert.Equal(original, await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(graph));
        Assert.Contains("\"test\":null", original, StringComparison.Ordinal);
        Assert.Null(evt.Properties["test"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreservesOrderedCaseDistinctAndDuplicatePropertyIdentity(bool duplicate)
    {
        var properties = duplicate
            ? new Dictionary<string, object> { ["nested"] = JsonSerializer.Deserialize<JsonElement>("{\"x\":1,\"x\":null,\"x\":2,\"drop\":null}") }
            : new Dictionary<string, object> { ["Foo"] = 1, ["foo"] = 2, ["drop"] = null! };
        var original = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(properties);
        Assert.Equal(duplicate ? "{\"nested\":{\"x\":1,\"x\":null,\"x\":2,\"drop\":null}}" : "{\"Foo\":1,\"foo\":2,\"drop\":null}", original);
        var evt = new CapturedEvent("test", "user", properties, DateTimeOffset.UtcNow);
        using var json = JsonDocument.Parse(await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(evt));
        var actual = json.RootElement.GetProperty("properties");
        if (duplicate)
            Assert.Equal("{\"x\":1,\"x\":2}", actual.GetProperty("nested").GetRawText());
        else
        {
            Assert.Equal(new[] { "Foo", "foo" }, actual.EnumerateObject().Take(2).Select(p => p.Name));
            Assert.Equal(1, actual.GetProperty("Foo").GetInt32());
            Assert.Equal(2, actual.GetProperty("foo").GetInt32());
            Assert.False(actual.TryGetProperty("drop", out _));
        }
        Assert.Equal(original, await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(properties));
    }

    [Fact]
    public async Task KeepsEnvelopeSerializationAndDeserializationContracts()
    {
        var evt = new CapturedEvent("test", "user", null, new DateTimeOffset(2025, 1, 1, 3, 0, 0, TimeSpan.FromHours(3)));
        var json = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(evt);
        Assert.Equal(JsonSerializer.Serialize(evt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), json);
        var restored = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<CapturedEvent>(json);
        Assert.NotNull(restored);
        Assert.Equal(evt.EventName, restored.EventName);
        Assert.Equal(evt.DistinctId, restored.DistinctId);
        Assert.Equal(evt.Timestamp, restored.Timestamp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureExceptionAndImmediateIdentifyCleanFinalHttpBytesAfterHooks(bool compression)
    {
        using var sink = new OfflineTransport();
        using var client = new PostHogClient(Options.Create(new PostHogOptions
        {
            ProjectToken = "test-token",
            HostUrl = new Uri("http://127.0.0.1:1"),
            EnableCompression = compression,
            FlushAt = 100,
            FlushInterval = TimeSpan.FromHours(1),
            SuperProperties = new Dictionary<string, object> { ["superNull"] = null! },
            BeforeSend = evt =>
            {
                if (evt.EventName == "drop") return null;
                evt.Properties["hookNull"] = null!;
                evt.Properties["hookItems"] = new object?[] { null, new { Drop = (object?)null } };
                return evt;
            }
        }), httpClientFactory: sink);
        Assert.True(client.Capture("user", "capture", Graph()));
        Assert.True(client.Capture("user", "only-null", new Dictionary<string, object> { ["test"] = null! }));
        Assert.True(client.Capture("user", "drop", Graph()));
        Assert.True(client.CaptureException(new InvalidOperationException("test"), "user", Graph()));
        await client.FlushAsync();
        await client.IdentifyAsync("user", Graph(), null, CancellationToken.None);
        Assert.Equal(2, sink.Bodies.Count);
        using var batch = JsonDocument.Parse(sink.Bodies[0]);
        Assert.Equal(3, batch.RootElement.GetProperty("batch").GetArrayLength());
        foreach (var evt in batch.RootElement.GetProperty("batch").EnumerateArray())
        {
            var props = evt.GetProperty("properties");
            Assert.False(props.TryGetProperty("test", out _));
            Assert.False(props.TryGetProperty("hookNull", out _));
            Assert.False(props.TryGetProperty("superNull", out _));
            Assert.Equal("[null,{}]", props.GetProperty("hookItems").GetRawText());
            Assert.True(props.TryGetProperty("$lib", out _));
            if (evt.GetProperty("event").GetString() != "only-null") AssertClean(props);
            if (evt.GetProperty("event").GetString() == "$exception") Assert.True(props.TryGetProperty("$exception_list", out _));
        }
        using var direct = JsonDocument.Parse(sink.Bodies[1]);
        AssertClean(direct.RootElement.GetProperty("properties").GetProperty("$set"));
        Assert.False(direct.RootElement.GetProperty("properties").TryGetProperty("superNull", out _));
    }

    [Theory]
    [InlineData("$exception", true)]
    [InlineData("custom", false)]
    public async Task PreservesOnlyExceptionEventsTypedMetadata(string eventName, bool preserve)
    {
        var evt = new CapturedEvent(eventName, "user", new Dictionary<string, object>
        {
            ["custom"] = new { Drop = (object?)null },
            ["$exception_list"] = new[] { new { Stacktrace = new { Frames = new[] { new { Filename = (string?)null } } } } }
        }, DateTimeOffset.UtcNow);
        using var json = JsonDocument.Parse(await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(evt));
        var props = json.RootElement.GetProperty("properties");
        Assert.Equal("{}", props.GetProperty("custom").GetRawText());
        Assert.Equal(preserve, props.GetProperty("$exception_list")[0].GetProperty("stacktrace").GetProperty("frames")[0].TryGetProperty("filename", out _));
    }

    sealed class OfflineTransport : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Bodies { get; } = new();
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2016:Forward the CancellationToken parameter", Justification = "These content/reader overloads also run on netcoreapp3.1.")]
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.True(request.RequestUri!.IsLoopback);
            Assert.Contains(request.RequestUri.AbsolutePath, new[] { "/batch", "/capture" });
            var bytes = await request.Content!.ReadAsByteArrayAsync();
            if (request.Content.Headers.ContentEncoding.Contains("gzip"))
            {
                using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
                using var reader = new StreamReader(gzip);
                Bodies.Add(await reader.ReadToEndAsync());
            }
            else Bodies.Add(Encoding.UTF8.GetString(bytes));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":1}") };
        }
    }

    [JsonConverter(typeof(JsonAwareValueConverter))]
    public sealed class JsonAwareValue { }
    public sealed class JsonAwareValueConverter : JsonConverter<JsonAwareValue>
    {
        public override JsonAwareValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, JsonAwareValue value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNull("drop");
            writer.WriteEndObject();
        }
    }
    [JsonConverter(typeof(JsonNullValueConverter))]
    public sealed class JsonNullValue { }
    public sealed class JsonNullValueConverter : JsonConverter<JsonNullValue>
    {
        public override JsonNullValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, JsonNullValue value, JsonSerializerOptions options) => writer.WriteNullValue();
    }
}
