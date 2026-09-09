using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace PostHog.AI.Tests;

public class NullPropertySerializationTests
{
    [Fact]
    public async Task HandlerContextAndHookPropertiesAreCleanedOnActualCoreWire()
    {
        using var transport = new OfflineTransport();
        using var posthog = new PostHogClient(Options.Create(new PostHogOptions
        {
            ProjectToken = "test-token",
            HostUrl = new Uri("http://127.0.0.1:1"),
            EnableCompression = false,
            FlushAt = 100,
            FlushInterval = TimeSpan.FromHours(1),
            BeforeSend = evt =>
            {
                evt.Properties["hookNull"] = null!;
                evt.Properties["hookItems"] = new object?[] { null, new { Drop = (object?)null } };
                return evt;
            }
        }), httpClientFactory: transport);
        using var handler = new PostHogOpenAIHandler(posthog, NullLogger<PostHogOpenAIHandler>.Instance)
        {
            InnerHandler = transport
        };
        using var provider = new HttpClient(handler);
        var custom = new Dictionary<string, object>
        {
            ["test"] = null!,
            ["items"] = new object?[] { "1", null, 2, new Dictionary<string, object> { ["drop"] = null! }, new object?[] { null } },
            ["nested"] = new { Drop = (object?)null, Keep = false }
        };
        using var scope = PostHogAIContext.BeginScope(distinctId: "user", properties: custom);
        using var request = new StringContent("{\"model\":\"test-model\",\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}]}");
        using var response = await provider.PostAsync(new Uri("http://127.0.0.1:1/v1/chat/completions"), request);
        Assert.True(response.IsSuccessStatusCode);
        await posthog.FlushAsync();
        using var json = JsonDocument.Parse(Assert.Single(transport.Events));
        var evt = json.RootElement.GetProperty("batch")[0];
        Assert.Equal("$ai_generation", evt.GetProperty("event").GetString());
        var properties = evt.GetProperty("properties");
        Assert.False(properties.TryGetProperty("test", out _));
        Assert.False(properties.TryGetProperty("hookNull", out _));
        Assert.Equal("[null,{}]", properties.GetProperty("hookItems").GetRawText());
        Assert.Equal("[\"1\",null,2,{},[null]]", properties.GetProperty("items").GetRawText());
        Assert.Equal("{\"keep\":false}", properties.GetProperty("nested").GetRawText());
        Assert.Null(custom["test"]);
    }

    sealed class OfflineTransport : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Events { get; } = new();
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.True(request.RequestUri!.IsLoopback);
            if (request.RequestUri.AbsolutePath == "/batch")
            {
                Events.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":1}") };
            }
            Assert.Equal("/v1/chat/completions", request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"test\",\"model\":\"test-model\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"hello\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1}}")
            };
        }
    }
}
