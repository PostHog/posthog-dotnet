using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PostHog;
using PostHog.AI.OpenAI;

namespace PostHog.AI.Tests;

public class PostHogOpenAIHandlerTests : IDisposable
{
    private readonly IPostHogClient _postHogClient;
    private readonly FakeHttpMessageHandler _fakeHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly PostHogOpenAIHandler _handler;

    public PostHogOpenAIHandlerTests()
    {
        _postHogClient = Substitute.For<IPostHogClient>();
        _fakeHttpMessageHandler = new FakeHttpMessageHandler();
        _handler = new PostHogOpenAIHandler(
            _postHogClient,
            NullLogger<PostHogOpenAIHandler>.Instance,
            Options.Create(new PostHogAIOptions())
        );

        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/"),
        };
        // Set the inner handler of the handler to our fake handler
        _handler.InnerHandler = _fakeHttpMessageHandler;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient?.Dispose();
            _handler?.Dispose();
            _fakeHttpMessageHandler?.Dispose();
            (_postHogClient as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task SendAsyncOpenAIRequestCapturesEvent()
    {
        // Arrange
        var requestUrl = new Uri("https://api.openai.com/v1/chat/completions");
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Hello" } },
        };
        var responseBody = new
        {
            id = "chatcmpl-123",
            choices = new[] { new { message = new { role = "assistant", content = "Hi there!" } } },
            usage = new
            {
                prompt_tokens = 10,
                completion_tokens = 5,
                total_tokens = 15,
            },
        };

        _fakeHttpMessageHandler.AddResponse(requestUrl, HttpMethod.Post, responseBody);

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            ),
        };
        using var response = await _httpClient.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify that PostHog client was called with expected event
        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(), // distinctId
                Arg.Is<string>(e => e == "$ai_generation"),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<GroupCollection?>(),
                Arg.Any<bool>()
            );
    }

    [Fact]
    public async Task SendAsyncNonOpenAIRequestDoesNotCapture()
    {
        // Arrange
        var requestUrl = new Uri("https://api.example.com/v1/other");
        _fakeHttpMessageHandler.AddResponse(requestUrl, HttpMethod.Get, new { message = "OK" });

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        using var response = await _httpClient.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _postHogClient
            .DidNotReceive()
            .Capture(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<GroupCollection?>(),
                Arg.Any<bool>()
            );
    }

    [Fact]
    public async Task SendAsyncWithPostHogParamsIncludesParamsInEvent()
    {
        // Arrange
        var requestUrl = new Uri("https://api.openai.com/v1/chat/completions");
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Hello" } },
            posthogDistinctId = "user-123",
            posthogTraceId = "trace-456",
            posthogProperties = new { environment = "test" },
        };
        var responseBody = new
        {
            id = "chatcmpl-123",
            choices = new[] { new { message = new { role = "assistant", content = "Hi" } } },
            usage = new
            {
                prompt_tokens = 5,
                completion_tokens = 3,
                total_tokens = 8,
            },
        };

        _fakeHttpMessageHandler.AddResponse(requestUrl, HttpMethod.Post, responseBody);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody, jsonOptions),
                Encoding.UTF8,
                "application/json"
            ),
        };
        var response = await _httpClient.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        _postHogClient
            .Received(1)
            .Capture(
                Arg.Is<string>(distinctId => distinctId == "user-123"),
                Arg.Any<string>(),
                Arg.Is<Dictionary<string, object>>(props =>
                    props.ContainsKey("$ai_trace_id")
                    && props["$ai_trace_id"] != null
                    && props["$ai_trace_id"].ToString() == "trace-456"
                ),
                Arg.Any<GroupCollection?>(),
                Arg.Any<bool>()
            );
    }

    [Fact]
    public async Task SendAsyncStreamingResponseCapturesEvent()
    {
        // Arrange
        var requestUrl = new Uri("https://api.openai.com/v1/chat/completions");
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Hello" } },
            stream = true,
        };

        // Create a streaming response with SSE format
        var streamData =
            "data: {\"id\":\"chatcmpl-123\",\"model\":\"gpt-4\",\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n\ndata: {\"id\":\"chatcmpl-123\",\"model\":\"gpt-4\",\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}\n\ndata: [DONE]\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(streamData));

        // Create a response with correct content type
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        };
        responseMessage.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");

        _fakeHttpMessageHandler.AddResponse(requestUrl, HttpMethod.Post, responseMessage);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            ),
        };
        var response = await _httpClient.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                Arg.Is<string>(e => e == "$ai_generation"),
                Arg.Is<Dictionary<string, object>>(props =>
                    props.ContainsKey("$ai_streaming") && true.Equals(props["$ai_streaming"])
                ),
                Arg.Any<GroupCollection?>(),
                Arg.Any<bool>()
            );
    }

    [Fact]
    public async Task SendAsyncAzureOpenAIRequestCapturesEvent()
    {
        // Arrange
        var requestUrl = new Uri(
            "https://my-resource.openai.azure.com/openai/deployments/gpt-4/chat/completions"
        );
        var requestBody = new
        {
            messages = new[] { new { role = "user", content = "Hello Azure" } },
        };
        var responseBody = new
        {
            id = "chatcmpl-azure-123",
            choices = new[]
            {
                new { message = new { role = "assistant", content = "Hi from Azure" } },
            },
            usage = new
            {
                prompt_tokens = 8,
                completion_tokens = 4,
                total_tokens = 12,
            },
        };

        _fakeHttpMessageHandler.AddResponse(requestUrl, HttpMethod.Post, responseBody);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody, jsonOptions),
                Encoding.UTF8,
                "application/json"
            ),
        };
        var response = await _httpClient.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                Arg.Is<string>(e => e == "$ai_generation"),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<GroupCollection?>(),
                Arg.Any<bool>()
            );
    }

    [Fact]
    public async Task SendAsyncWithErrorCapturesErrorEvent()
    {
        // Arrange
        var requestUrl = new Uri("https://api.openai.com/v1/chat/completions");
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Hello" } },
        };

        // Simulate an error response
        var errorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"Internal server error\"}}",
                Encoding.UTF8,
                "application/json"
            ),
        };
        _fakeHttpMessageHandler.AddResponse(requestUrl, HttpMethod.Post, errorResponse);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            ),
        };
        var response = await _httpClient.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                Arg.Is<string>(e => e == "$ai_generation"),
                Arg.Is<Dictionary<string, object>>(props =>
                    props.ContainsKey("$ai_is_error") && true.Equals(props["$ai_is_error"])
                ),
                Arg.Any<GroupCollection?>(),
                Arg.Any<bool>()
            );
    }
}
