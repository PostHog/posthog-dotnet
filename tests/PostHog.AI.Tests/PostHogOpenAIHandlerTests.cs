using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace PostHog.AI.Tests;

public sealed class PostHogOpenAIHandlerTests : IDisposable
{
    private readonly IPostHogClient _postHogClient;
    private readonly PostHogOpenAIHandler _handler;
    private readonly HttpClient _client;
    private readonly MockHttpMessageHandler _innerHandler;

    public PostHogOpenAIHandlerTests()
    {
        _postHogClient = Substitute.For<IPostHogClient>();
        _postHogClient
            .Capture(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<GroupCollection?>(),
                Arg.Any<bool>(),
                Arg.Any<DateTimeOffset?>()
            )
            .Returns(true);

        var mockLogger = Substitute.For<ILogger<PostHogOpenAIHandler>>();
        _handler = new PostHogOpenAIHandler(_postHogClient, mockLogger);
        _innerHandler = new MockHttpMessageHandler();
        _handler.InnerHandler = _innerHandler;

        _client = new HttpClient(_handler) { BaseAddress = new Uri("https://api.openai.com") };
    }

    [Fact]
    public async Task SendAsyncCapturesEventOnSuccess()
    {
        // Arrange
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Hello" } },
        };

        var responseBody = new
        {
            id = "chatcmpl-123",
            @object = "chat.completion",
            created = 1677652288,
            model = "gpt-4-0613",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = "Hi there!" },
                    finish_reason = "stop",
                },
            },
            usage = new
            {
                prompt_tokens = 9,
                completion_tokens = 12,
                total_tokens = 21,
            },
        };

        using var responseContent = new StringContent(
            JsonSerializer.Serialize(responseBody),
            Encoding.UTF8,
            "application/json"
        );
        _innerHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = responseContent,
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        // Act
        using var response = await _client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            requestContent
        );

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                PostHogAIFieldNames.Generation,
                Arg.Is<Dictionary<string, object>>(props => VerifyProps(props)),
                null,
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    [Fact]
    public async Task SendAsyncCapturesEmbeddingEvent()
    {
        // Arrange
        var requestBody = new
        {
            model = "text-embedding-ada-002",
            input = "The quick brown fox",
        };

        var responseBody = new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    @object = "embedding",
                    index = 0,
                    embedding = new[] { 0.0023, -0.0012 },
                },
            },
            model = "text-embedding-ada-002-v2",
            usage = new { prompt_tokens = 5, total_tokens = 5 },
        };

        using var responseContent = new StringContent(
            JsonSerializer.Serialize(responseBody),
            Encoding.UTF8,
            "application/json"
        );
        _innerHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = responseContent,
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        // Act
        using var response = await _client.PostAsync(
            new Uri("/v1/embeddings", UriKind.Relative),
            requestContent
        );

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                PostHogAIFieldNames.Embedding,
                Arg.Is<Dictionary<string, object>>(props =>
                    (string)props[PostHogAIFieldNames.Model] == "text-embedding-ada-002-v2"
                    && (string)props[PostHogAIFieldNames.Input] == "The quick brown fox"
                    && (int)props[PostHogAIFieldNames.InputTokens] == 5
                    && (string)props[PostHogAIFieldNames.Provider] == "openai"
                ),
                null,
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    [Fact]
    public async Task SendAsyncCapturesStreamingEvent()
    {
        // Arrange
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Tell me a joke" } },
            stream = true,
        };

        // Simulate SSE stream
        var sseStream = new MemoryStream();
        using var writer = new StreamWriter(
            sseStream,
            new UTF8Encoding(false),
            1024,
            leaveOpen: true
        );
        await writer.WriteAsync(
            "data: {\"choices\": [{\"index\": 0, \"delta\": {\"content\": \"Why did\"}}]}\n\n"
        );
        await writer.WriteAsync(
            "data: {\"choices\": [{\"index\": 0, \"delta\": {\"content\": \" the chicken\"}}]}\n\n"
        );
        await writer.WriteAsync(
            "data: {\"choices\": [{\"index\": 0, \"delta\": {\"content\": \" cross output\"}}]}\n\n"
        );
        await writer.WriteAsync(
            "data: {\"usage\": {\"prompt_tokens\": 10, \"completion_tokens\": 5, \"total_tokens\": 15}}\n\n"
        );
        await writer.WriteAsync("data: [DONE]\n\n");
        await writer.FlushAsync();
        sseStream.Position = 0;

        var streamContent = new StreamContent(sseStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");

        _innerHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = streamContent,
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        // Act
        HttpResponseMessage? response = null;
        try
        {
            response = await _client.PostAsync(
                new Uri("/v1/chat/completions", UriKind.Relative),
                requestContent
            );

            Assert.True(response.IsSuccessStatusCode);

            // Consume the stream - TrackingStream callback fires on disposal
            var resultStream = await response.Content.ReadAsStreamAsync();
            using (var reader = new StreamReader(resultStream))
            {
                await reader.ReadToEndAsync();
            }
        }
        finally
        {
            response?.Dispose();
        }

        // Assert
        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                PostHogAIFieldNames.Generation,
                Arg.Is<Dictionary<string, object>>(props => VerifyStreamingProps(props)),
                null,
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    private static bool VerifyStreamingProps(Dictionary<string, object> props)
    {
        if (
            !props.TryGetValue(PostHogAIFieldNames.OutputChoices, out var choicesObj)
            || choicesObj is not JsonArray choices
        )
            return false;

        var content = choices[0]?["message"]?["content"]?.ToString();
        if (content != "Why did the chicken cross output")
            return false;

        if (
            !props.TryGetValue(PostHogAIFieldNames.InputTokens, out var inputTokens)
            || (int)inputTokens != 10
        )
            return false;
        if (
            !props.TryGetValue(PostHogAIFieldNames.OutputTokens, out var outputTokens)
            || (int)outputTokens != 5
        )
            return false;

        return true;
    }

    private static bool VerifyProps(Dictionary<string, object> props)
    {
        if (
            !props.TryGetValue(PostHogAIFieldNames.Model, out var model)
            || (string)model != "gpt-4-0613"
        )
            return false;
        if (
            !props.TryGetValue(PostHogAIFieldNames.InputTokens, out var inputTokens)
            || (int)inputTokens != 9
        )
            return false;
        if (
            !props.TryGetValue(PostHogAIFieldNames.OutputTokens, out var outputTokens)
            || (int)outputTokens != 12
        )
            return false;
        if (
            !props.TryGetValue(PostHogAIFieldNames.Provider, out var provider)
            || (string)provider != "openai"
        )
            return false;
        return true;
    }

    [Fact]
    public async Task SendAsyncExcludesInputAndOutputChoicesWhenPrivacyModeIsTrueSimpleMessages()
    {
        // Arrange
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Hello" } },
        };

        var responseBody = new
        {
            id = "chatcmpl-123",
            @object = "chat.completion",
            created = 1677652288,
            model = "gpt-4-0613",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = "Hi there!" },
                    finish_reason = "stop",
                },
            },
            usage = new
            {
                prompt_tokens = 9,
                completion_tokens = 12,
                total_tokens = 21,
            },
        };

        using var responseContent = new StringContent(
            JsonSerializer.Serialize(responseBody),
            Encoding.UTF8,
            "application/json"
        );
        _innerHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = responseContent,
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        // Act
        using (PostHogAIContext.BeginScope(privacyMode: true))
        {
            using var response = await _client.PostAsync(
                new Uri("/v1/chat/completions", UriKind.Relative),
                requestContent
            );

            Assert.True(response.IsSuccessStatusCode);
        }

        // Assert
        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                PostHogAIFieldNames.Generation,
                Arg.Is<Dictionary<string, object>>(props =>
                    !props.ContainsKey(PostHogAIFieldNames.Input)
                    && !props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                ),
                null,
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    [Fact]
    public async Task SendAsyncIncludesInputAndOutputChoicesWhenPrivacyModeIsFalseSimpleMessages()
    {
        // Arrange
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Hello" } },
        };

        var responseBody = new
        {
            id = "chatcmpl-123",
            @object = "chat.completion",
            created = 1677652288,
            model = "gpt-4-0613",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = "Hi there!" },
                    finish_reason = "stop",
                },
            },
            usage = new
            {
                prompt_tokens = 9,
                completion_tokens = 12,
                total_tokens = 21,
            },
        };

        using var responseContent = new StringContent(
            JsonSerializer.Serialize(responseBody),
            Encoding.UTF8,
            "application/json"
        );
        _innerHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = responseContent,
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        // Act
        using (PostHogAIContext.BeginScope(privacyMode: false))
        {
            using var response = await _client.PostAsync(
                new Uri("/v1/chat/completions", UriKind.Relative),
                requestContent
            );

            Assert.True(response.IsSuccessStatusCode);
        }

        // Assert
        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                PostHogAIFieldNames.Generation,
                Arg.Is<Dictionary<string, object>>(props =>
                    props.ContainsKey(PostHogAIFieldNames.Input)
                    && props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                ),
                null,
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    [Fact]
    public async Task SendAsyncExcludesInputWhenPrivacyModeIsTrueEmbeddings()
    {
        // Arrange
        var requestBody = new
        {
            model = "text-embedding-ada-002",
            input = "The quick brown fox",
        };

        var responseBody = new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    @object = "embedding",
                    index = 0,
                    embedding = new[] { 0.0023, -0.0012 },
                },
            },
            model = "text-embedding-ada-002-v2",
            usage = new { prompt_tokens = 5, total_tokens = 5 },
        };

        using var responseContent = new StringContent(
            JsonSerializer.Serialize(responseBody),
            Encoding.UTF8,
            "application/json"
        );
        _innerHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = responseContent,
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        // Act
        using (PostHogAIContext.BeginScope(privacyMode: true))
        {
            using var response = await _client.PostAsync(
                new Uri("/v1/embeddings", UriKind.Relative),
                requestContent
            );

            Assert.True(response.IsSuccessStatusCode);
        }

        // Assert
        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                PostHogAIFieldNames.Embedding,
                Arg.Is<Dictionary<string, object>>(props =>
                    !props.ContainsKey(PostHogAIFieldNames.Input)
                    && (string)props[PostHogAIFieldNames.Model] == "text-embedding-ada-002-v2"
                    && (int)props[PostHogAIFieldNames.InputTokens] == 5
                ),
                null,
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    [Fact]
    public async Task SendAsyncExcludesInputAndOutputChoicesWhenPrivacyModeIsTrueStreaming()
    {
        // Arrange
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Tell me a joke" } },
            stream = true,
        };

        var sseStream = new MemoryStream();
        using var writer = new StreamWriter(
            sseStream,
            new UTF8Encoding(false),
            1024,
            leaveOpen: true
        );
        await writer.WriteAsync(
            "data: {\"choices\": [{\"index\": 0, \"delta\": {\"content\": \"Why did\"}}]}\n\n"
        );
        await writer.WriteAsync(
            "data: {\"choices\": [{\"index\": 0, \"delta\": {\"content\": \" the chicken\"}}]}\n\n"
        );
        await writer.WriteAsync(
            "data: {\"choices\": [{\"index\": 0, \"delta\": {\"content\": \" cross output\"}}]}\n\n"
        );
        await writer.WriteAsync(
            "data: {\"usage\": {\"prompt_tokens\": 10, \"completion_tokens\": 5, \"total_tokens\": 15}}\n\n"
        );
        await writer.WriteAsync("data: [DONE]\n\n");
        await writer.FlushAsync();
        sseStream.Position = 0;

        var streamContent = new StreamContent(sseStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");

        _innerHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = streamContent,
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        // Act - Context is captured eagerly in SendAsync, so scope can be exited before stream disposal
        HttpResponseMessage? response = null;
        using (PostHogAIContext.BeginScope(privacyMode: true))
        {
            try
            {
                response = await _client.PostAsync(
                    new Uri("/v1/chat/completions", UriKind.Relative),
                    requestContent
                );

                Assert.True(response.IsSuccessStatusCode);

                var resultStream = await response.Content.ReadAsStreamAsync();
                using (var reader = new StreamReader(resultStream))
                {
                    await reader.ReadToEndAsync();
                }
            }
            finally
            {
                response?.Dispose();
            }
        }

        // Assert
        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                PostHogAIFieldNames.Generation,
                Arg.Is<Dictionary<string, object>>(props =>
                    !props.ContainsKey(PostHogAIFieldNames.Input)
                    && !props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                    && (int)props[PostHogAIFieldNames.InputTokens] == 10
                    && (int)props[PostHogAIFieldNames.OutputTokens] == 5
                ),
                null,
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    [Fact]
    public async Task SendAsyncIncludesInputAndOutputChoicesWhenPrivacyModeIsNullSimpleMessages()
    {
        // Arrange
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Hello" } },
        };

        var responseBody = new
        {
            id = "chatcmpl-123",
            @object = "chat.completion",
            created = 1677652288,
            model = "gpt-4-0613",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = "Hi there!" },
                    finish_reason = "stop",
                },
            },
            usage = new
            {
                prompt_tokens = 9,
                completion_tokens = 12,
                total_tokens = 21,
            },
        };

        using var responseContent = new StringContent(
            JsonSerializer.Serialize(responseBody),
            Encoding.UTF8,
            "application/json"
        );
        _innerHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = responseContent,
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        // Act - No privacy mode scope
        using var response = await _client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            requestContent
        );

        Assert.True(response.IsSuccessStatusCode);

        // Assert
        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                PostHogAIFieldNames.Generation,
                Arg.Is<Dictionary<string, object>>(props =>
                    props.ContainsKey(PostHogAIFieldNames.Input)
                    && props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                ),
                null,
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    [Fact]
    public async Task SendAsyncCapturesErrorEventOnNetworkException()
    {
        // Arrange
        _innerHandler.ExceptionToThrow = new HttpRequestException("Connection refused");

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(
                new
                {
                    model = "gpt-4",
                    messages = new[] { new { role = "user", content = "Hello" } },
                }
            ),
            Encoding.UTF8,
            "application/json"
        );

        // Act & Assert - should rethrow the exception
        await Assert.ThrowsAsync<HttpRequestException>(
            () =>
                _client.PostAsync(
                    new Uri("/v1/chat/completions", UriKind.Relative),
                    requestContent
                )
        );

        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                PostHogAIFieldNames.Generation,
                Arg.Is<Dictionary<string, object>>(props =>
                    (bool)props[PostHogAIFieldNames.IsError] == true
                    && (string)props[PostHogAIFieldNames.Error] == "Connection refused"
                ),
                null,
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    [Fact]
    public async Task SendAsyncCapturesErrorPropertiesOnHttp429()
    {
        // Arrange
        var errorResponse = new
        {
            error = new { message = "Rate limit exceeded", type = "rate_limit_error" },
        };
        using var responseContent = new StringContent(
            JsonSerializer.Serialize(errorResponse),
            Encoding.UTF8,
            "application/json"
        );
        _innerHandler.Response = new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = responseContent,
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(
                new
                {
                    model = "gpt-4",
                    messages = new[] { new { role = "user", content = "Hello" } },
                }
            ),
            Encoding.UTF8,
            "application/json"
        );

        // Act
        using var response = await _client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            requestContent
        );

        // Assert
        Assert.Equal((HttpStatusCode)429, response.StatusCode);

        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                PostHogAIFieldNames.Generation,
                Arg.Is<Dictionary<string, object>>(props =>
                    (bool)props[PostHogAIFieldNames.IsError] == true
                    && (int)props[PostHogAIFieldNames.HttpStatus] == 429
                ),
                null,
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    [Fact]
    public async Task SendAsyncHandlesGracefullyOnMalformedJsonResponse()
    {
        // Arrange
        using var responseContent = new StringContent(
            "this is not json {{{{",
            Encoding.UTF8,
            "application/json"
        );
        _innerHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = responseContent,
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(
                new
                {
                    model = "gpt-4",
                    messages = new[] { new { role = "user", content = "Hello" } },
                }
            ),
            Encoding.UTF8,
            "application/json"
        );

        // Act - should not throw; handler should gracefully handle malformed JSON
        using var response = await _client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            requestContent
        );

        Assert.True(response.IsSuccessStatusCode);

        // Event should still be captured (with whatever properties could be extracted)
        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, object>>(),
                null,
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    [Fact]
    public async Task SendAsyncPassesGroupsToCaptureWhenContextHasGroups()
    {
        // Arrange
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Hello" } },
        };

        var responseBody = new
        {
            id = "chatcmpl-123",
            @object = "chat.completion",
            created = 1677652288,
            model = "gpt-4-0613",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = "Hi!" },
                    finish_reason = "stop",
                },
            },
            usage = new
            {
                prompt_tokens = 9,
                completion_tokens = 3,
                total_tokens = 12,
            },
        };

        using var responseContent = new StringContent(
            JsonSerializer.Serialize(responseBody),
            Encoding.UTF8,
            "application/json"
        );
        _innerHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = responseContent,
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        // Act - set groups in context
        using (
            PostHogAIContext.BeginScope(
                groups: new Dictionary<string, object> { { "company", "acme-corp" } }
            )
        )
        {
            using var response = await _client.PostAsync(
                new Uri("/v1/chat/completions", UriKind.Relative),
                requestContent
            );

            Assert.True(response.IsSuccessStatusCode);
        }

        // Assert - groups should be passed as GroupCollection
        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                PostHogAIFieldNames.Generation,
                Arg.Any<Dictionary<string, object>>(),
                Arg.Is<GroupCollection>(g => g != null && g.Count == 1),
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    [Fact]
    public async Task SendAsyncContextPropertiesOverrideEventProperties()
    {
        // Arrange
        var requestBody = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Hello" } },
        };

        var responseBody = new
        {
            id = "chatcmpl-123",
            @object = "chat.completion",
            created = 1677652288,
            model = "gpt-4-0613",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = "Hi!" },
                    finish_reason = "stop",
                },
            },
            usage = new
            {
                prompt_tokens = 9,
                completion_tokens = 3,
                total_tokens = 12,
            },
        };

        using var responseContent = new StringContent(
            JsonSerializer.Serialize(responseBody),
            Encoding.UTF8,
            "application/json"
        );
        _innerHandler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = responseContent,
        };

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        // Act - set custom properties via context
        using (
            PostHogAIContext.BeginScope(
                properties: new Dictionary<string, object>
                {
                    { "custom_prop", "custom_value" },
                }
            )
        )
        {
            using var response = await _client.PostAsync(
                new Uri("/v1/chat/completions", UriKind.Relative),
                requestContent
            );

            Assert.True(response.IsSuccessStatusCode);
        }

        // Assert - properties should be merged without throwing
        _postHogClient
            .Received(1)
            .Capture(
                Arg.Any<string>(),
                PostHogAIFieldNames.Generation,
                Arg.Is<Dictionary<string, object>>(props =>
                    (string)props["custom_prop"] == "custom_value"
                ),
                null,
                false,
                Arg.Any<DateTimeOffset?>()
            );
    }

    [Fact]
    public void AddPostHogOpenAIClientThrowsOnNullOrEmptyApiKey()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddPostHogOpenAIClient(""));
        Assert.Throws<ArgumentException>(() => services.AddPostHogOpenAIClient("   "));
    }

    [Fact]
    public void AddPostHogOpenAIClientThrowsWhenPostHogNotRegistered()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddPostHogOpenAIClient("sk-test-key"));
    }

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
        _innerHandler.Dispose();
        _postHogClient.Dispose();
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
        public Exception? ExceptionToThrow { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(Response);
        }
    }
}
