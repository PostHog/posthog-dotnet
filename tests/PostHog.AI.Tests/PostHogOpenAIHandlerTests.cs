using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Moq;

namespace PostHog.AI.Tests;

public sealed class PostHogOpenAIHandlerTests : IDisposable
{
    private readonly Mock<IPostHogClient> _mockPostHogClient;
    private readonly PostHogOpenAIHandler _handler;
    private readonly HttpClient _client;
    private readonly MockHttpMessageHandler _innerHandler;

    public PostHogOpenAIHandlerTests()
    {
        _mockPostHogClient = new Mock<IPostHogClient>();
        var mockLogger = new Mock<ILogger<PostHogOpenAIHandler>>();
        _handler = new PostHogOpenAIHandler(_mockPostHogClient.Object, mockLogger.Object);
        _innerHandler = new MockHttpMessageHandler();
        _handler.InnerHandler = _innerHandler;

        _client = new HttpClient(_handler) { BaseAddress = new Uri("https://api.openai.com") };
    }

    [Fact]
    public async Task SendAsyncCapturesEventOnSuccess()
    {
        // Arrange
        var captureSignal = new ManualResetEventSlim(false);
        try
        {
            // Use local variable to avoid capture warning - signal is disposed only after we wait for it
            var signal = captureSignal;
            _mockPostHogClient
                .Setup(x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Generation,
                        It.Is<Dictionary<string, object>>(props => VerifyProps(props)),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    )
                )
                .Returns(true)
                .Callback(() => signal.Set());

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

            // Wait for background task to complete (fire and forget)
            Assert.True(
                captureSignal.Wait(TimeSpan.FromSeconds(5)),
                "Capture was not called within the expected time"
            );

            _mockPostHogClient.Verify(
                x =>
                    x.Capture(
                        It.IsAny<string>(), // distinctId
                        PostHogAIFieldNames.Generation,
                        It.Is<Dictionary<string, object>>(props => VerifyProps(props)),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    ),
                Times.Once
            );
        }
        finally
        {
            // Dispose after we've confirmed the callback was called (by waiting for the signal)
            captureSignal.Dispose();
        }
    }

    [Fact]
    public async Task SendAsyncCapturesEmbeddingEvent()
    {
        // Arrange
        var captureSignal = new ManualResetEventSlim(false);
        try
        {
            // Use local variable to avoid capture warning - signal is disposed only after we wait for it
            var signal = captureSignal;
            _mockPostHogClient
                .Setup(x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Embedding,
                        It.Is<Dictionary<string, object>>(props =>
                            (string)props[PostHogAIFieldNames.Model] == "text-embedding-ada-002-v2"
                            && (string)props[PostHogAIFieldNames.Input] == "The quick brown fox"
                            && (int)props[PostHogAIFieldNames.InputTokens] == 5
                            && (string)props[PostHogAIFieldNames.Provider] == "openai"
                        ),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    )
                )
                .Returns(true)
                .Callback(() => signal.Set());

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

            // Wait for background task to complete (fire and forget)
            Assert.True(
                captureSignal.Wait(TimeSpan.FromSeconds(5)),
                "Capture was not called within the expected time"
            );

            _mockPostHogClient.Verify(
                x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Embedding,
                        It.Is<Dictionary<string, object>>(props =>
                            (string)props[PostHogAIFieldNames.Model] == "text-embedding-ada-002-v2"
                            && (string)props[PostHogAIFieldNames.Input] == "The quick brown fox"
                            && (int)props[PostHogAIFieldNames.InputTokens] == 5
                            && (string)props[PostHogAIFieldNames.Provider] == "openai"
                        ),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    ),
                Times.Once
            );
        }
        finally
        {
            // Dispose after we've confirmed the callback was called (by waiting for the signal)
            captureSignal.Dispose();
        }
    }

    [Fact]
    public async Task SendAsyncCapturesStreamingEvent()
    {
        // Arrange
        var captureSignal = new ManualResetEventSlim(false);
        try
        {
            // Use local variable to avoid capture warning - signal is disposed only after we wait for it
            var signal = captureSignal;
            _mockPostHogClient
                .Setup(x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Generation,
                        It.Is<Dictionary<string, object>>(props => VerifyStreamingProps(props)),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    )
                )
                .Returns(true)
                .Callback(() => signal.Set());

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
            // Usage usually comes in a final chunk or separate message depending on provider,
            // standard OpenAI often doesn't send usage in stream unless 'stream_options: {"include_usage": true}' is set.
            // Let's simulate a usage chunk.
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
            // We must read the stream to trigger the capture
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
                // StreamReader disposal will dispose the underlying stream (TrackingStream),
                // which triggers the capture callback in its Dispose method
            }
            finally
            {
                // Assert
                // TrackingStream callback fires on disposal, so we need to dispose the response
                // to ensure the stream is disposed and the capture is triggered
                response?.Dispose();
            }

            // Wait for background task to complete (fire and forget)
            Assert.True(
                captureSignal.Wait(TimeSpan.FromSeconds(5)),
                "Capture was not called within the expected time"
            );

            _mockPostHogClient.Verify(
                x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Generation,
                        It.Is<Dictionary<string, object>>(props => VerifyStreamingProps(props)),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    ),
                Times.Once
            );
        }
        finally
        {
            // Dispose after we've confirmed the callback was called (by waiting for the signal)
            captureSignal.Dispose();
        }
    }

    private static bool VerifyStreamingProps(Dictionary<string, object> props)
    {
        if (
            !props.TryGetValue(PostHogAIFieldNames.OutputChoices, out var choicesObj)
            || !(choicesObj is JsonArray choices)
        )
            return false;

        // We constructed a simplified choices array in the handler
        // [{"message": {"role": "assistant", "content": "Why did the chicken cross output"}}]
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
        var captureSignal = new ManualResetEventSlim(false);
        try
        {
            var signal = captureSignal;
            _mockPostHogClient
                .Setup(x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Generation,
                        It.Is<Dictionary<string, object>>(props =>
                            !props.ContainsKey(PostHogAIFieldNames.Input)
                            && !props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                        ),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    )
                )
                .Returns(true)
                .Callback(() => signal.Set());

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

            // Act - Use privacy mode scope
            using (PostHogAIContext.BeginScope(privacyMode: true))
            {
                using var response = await _client.PostAsync(
                    new Uri("/v1/chat/completions", UriKind.Relative),
                    requestContent
                );

                Assert.True(response.IsSuccessStatusCode);
            }

            // Assert
            Assert.True(
                captureSignal.Wait(TimeSpan.FromSeconds(5)),
                "Capture was not called within the expected time"
            );

            _mockPostHogClient.Verify(
                x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Generation,
                        It.Is<Dictionary<string, object>>(props =>
                            !props.ContainsKey(PostHogAIFieldNames.Input)
                            && !props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                        ),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    ),
                Times.Once
            );
        }
        finally
        {
            captureSignal.Dispose();
        }
    }

    [Fact]
    public async Task SendAsyncIncludesInputAndOutputChoicesWhenPrivacyModeIsFalseSimpleMessages()
    {
        // Arrange
        var captureSignal = new ManualResetEventSlim(false);
        try
        {
            var signal = captureSignal;
            _mockPostHogClient
                .Setup(x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Generation,
                        It.Is<Dictionary<string, object>>(props =>
                            props.ContainsKey(PostHogAIFieldNames.Input)
                            && props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                        ),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    )
                )
                .Returns(true)
                .Callback(() => signal.Set());

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

            // Act - Use privacy mode explicitly set to false
            using (PostHogAIContext.BeginScope(privacyMode: false))
            {
                using var response = await _client.PostAsync(
                    new Uri("/v1/chat/completions", UriKind.Relative),
                    requestContent
                );

                Assert.True(response.IsSuccessStatusCode);
            }

            // Assert
            Assert.True(
                captureSignal.Wait(TimeSpan.FromSeconds(5)),
                "Capture was not called within the expected time"
            );

            _mockPostHogClient.Verify(
                x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Generation,
                        It.Is<Dictionary<string, object>>(props =>
                            props.ContainsKey(PostHogAIFieldNames.Input)
                            && props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                        ),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    ),
                Times.Once
            );
        }
        finally
        {
            captureSignal.Dispose();
        }
    }

    [Fact]
    public async Task SendAsyncExcludesInputWhenPrivacyModeIsTrueEmbeddings()
    {
        // Arrange
        var captureSignal = new ManualResetEventSlim(false);
        try
        {
            var signal = captureSignal;
            _mockPostHogClient
                .Setup(x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Embedding,
                        It.Is<Dictionary<string, object>>(props =>
                            !props.ContainsKey(PostHogAIFieldNames.Input)
                            && (string)props[PostHogAIFieldNames.Model] == "text-embedding-ada-002-v2"
                            && (int)props[PostHogAIFieldNames.InputTokens] == 5
                        ),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    )
                )
                .Returns(true)
                .Callback(() => signal.Set());

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

            // Act - Use privacy mode scope
            using (PostHogAIContext.BeginScope(privacyMode: true))
            {
                using var response = await _client.PostAsync(
                    new Uri("/v1/embeddings", UriKind.Relative),
                    requestContent
                );

                Assert.True(response.IsSuccessStatusCode);
            }

            // Assert
            Assert.True(
                captureSignal.Wait(TimeSpan.FromSeconds(5)),
                "Capture was not called within the expected time"
            );

            _mockPostHogClient.Verify(
                x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Embedding,
                        It.Is<Dictionary<string, object>>(props =>
                            !props.ContainsKey(PostHogAIFieldNames.Input)
                            && (string)props[PostHogAIFieldNames.Model] == "text-embedding-ada-002-v2"
                            && (int)props[PostHogAIFieldNames.InputTokens] == 5
                        ),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    ),
                Times.Once
            );
        }
        finally
        {
            captureSignal.Dispose();
        }
    }

    [Fact]
    public async Task SendAsyncExcludesInputAndOutputChoicesWhenPrivacyModeIsTrueStreaming()
    {
        // Arrange
        var captureSignal = new ManualResetEventSlim(false);
        try
        {
            var signal = captureSignal;
            _mockPostHogClient
                .Setup(x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Generation,
                        It.Is<Dictionary<string, object>>(props =>
                            !props.ContainsKey(PostHogAIFieldNames.Input)
                            && !props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                            && (int)props[PostHogAIFieldNames.InputTokens] == 10
                            && (int)props[PostHogAIFieldNames.OutputTokens] == 5
                        ),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    )
                )
                .Returns(true)
                .Callback(() => signal.Set());

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

            // Act - Use privacy mode scope
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

                    // Consume the stream - TrackingStream callback fires on disposal
                    var resultStream = await response.Content.ReadAsStreamAsync();
                    using (var reader = new StreamReader(resultStream))
                    {
                        await reader.ReadToEndAsync();
                    }
                }
                finally
                {
                    // Dispose response while scope is still active
                    response?.Dispose();
                }
            }

            // Assert
            Assert.True(
                captureSignal.Wait(TimeSpan.FromSeconds(5)),
                "Capture was not called within the expected time"
            );

            _mockPostHogClient.Verify(
                x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Generation,
                        It.Is<Dictionary<string, object>>(props =>
                            !props.ContainsKey(PostHogAIFieldNames.Input)
                            && !props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                            && (int)props[PostHogAIFieldNames.InputTokens] == 10
                            && (int)props[PostHogAIFieldNames.OutputTokens] == 5
                        ),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    ),
                Times.Once
            );
        }
        finally
        {
            captureSignal.Dispose();
        }
    }

    [Fact]
    public async Task SendAsyncIncludesInputAndOutputChoicesWhenPrivacyModeIsNullSimpleMessages()
    {
        // Arrange
        var captureSignal = new ManualResetEventSlim(false);
        try
        {
            var signal = captureSignal;
            _mockPostHogClient
                .Setup(x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Generation,
                        It.Is<Dictionary<string, object>>(props =>
                            props.ContainsKey(PostHogAIFieldNames.Input)
                            && props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                        ),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    )
                )
                .Returns(true)
                .Callback(() => signal.Set());

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

            // Act - No privacy mode scope (null by default)
            using var response = await _client.PostAsync(
                new Uri("/v1/chat/completions", UriKind.Relative),
                requestContent
            );

            Assert.True(response.IsSuccessStatusCode);

            // Assert
            Assert.True(
                captureSignal.Wait(TimeSpan.FromSeconds(5)),
                "Capture was not called within the expected time"
            );

            _mockPostHogClient.Verify(
                x =>
                    x.Capture(
                        It.IsAny<string>(),
                        PostHogAIFieldNames.Generation,
                        It.Is<Dictionary<string, object>>(props =>
                            props.ContainsKey(PostHogAIFieldNames.Input)
                            && props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                        ),
                        null,
                        false,
                        It.IsAny<DateTimeOffset?>()
                    ),
                Times.Once
            );
        }
        finally
        {
            captureSignal.Dispose();
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
        _innerHandler.Dispose();
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(Response);
        }
    }
}
