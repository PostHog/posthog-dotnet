using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace PostHog.AI.Tests;

public class PostHogChatClientTests
{
    private readonly Mock<IPostHogClient> _mockPostHogClient;
    private readonly Mock<ILogger<PostHogChatClient>> _mockLogger;

    public PostHogChatClientTests()
    {
        _mockPostHogClient = new Mock<IPostHogClient>();
        _mockLogger = new Mock<ILogger<PostHogChatClient>>();
    }

    [Fact]
    public async Task GetResponseAsyncCapturesGenerationEvent()
    {
        // Arrange
        var expectedResponse = new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, "Hello!")]
        )
        {
            ModelId = "gpt-4-0613",
            Usage = new UsageDetails
            {
                InputTokenCount = 9,
                OutputTokenCount = 12,
                TotalTokenCount = 21,
            },
        };

        using var innerClient = new TestChatClient { ResponseToReturn = expectedResponse };

        using var client = new PostHogChatClient(
            innerClient,
            _mockPostHogClient.Object,
            _mockLogger.Object
        );

        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        // Act
        var response = await client.GetResponseAsync(
            messages,
            new ChatOptions { ModelId = "gpt-4" }
        );

        // Assert
        Assert.Equal("Hello!", response.Messages[0].Text);

        _mockPostHogClient.Verify(
            x =>
                x.Capture(
                    It.IsAny<string>(),
                    PostHogAIFieldNames.Generation,
                    It.Is<Dictionary<string, object>>(props =>
                        (string)props[PostHogAIFieldNames.Model] == "gpt-4-0613"
                        && (long)props[PostHogAIFieldNames.InputTokens] == 9
                        && (long)props[PostHogAIFieldNames.OutputTokens] == 12
                        && (long)props[PostHogAIFieldNames.TotalTokens] == 21
                        && (string)props[PostHogAIFieldNames.Provider] == "openai"
                        && (string)props[PostHogAIFieldNames.Lib] == "posthog-dotnet"
                    ),
                    null,
                    false,
                    It.IsAny<DateTimeOffset?>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task GetResponseAsyncCapturesErrorOnException()
    {
        // Arrange
        using var innerClient = new TestChatClient
        {
            ExceptionToThrow = new InvalidOperationException("Service unavailable"),
        };

        using var client = new PostHogChatClient(
            innerClient,
            _mockPostHogClient.Object,
            _mockLogger.Object
        );

        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetResponseAsync(messages, new ChatOptions { ModelId = "gpt-4" })
        );

        _mockPostHogClient.Verify(
            x =>
                x.Capture(
                    It.IsAny<string>(),
                    PostHogAIFieldNames.Generation,
                    It.Is<Dictionary<string, object>>(props =>
                        (bool)props[PostHogAIFieldNames.IsError] == true
                        && (string)props[PostHogAIFieldNames.Error] == "Service unavailable"
                    ),
                    null,
                    false,
                    It.IsAny<DateTimeOffset?>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task GetStreamingResponseAsyncCapturesAfterCompletion()
    {
        // Arrange
        var updates = new List<ChatResponseUpdate>
        {
            new()
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent("Hello")],
                ModelId = "gpt-4",
            },
            new()
            {
                Contents =
                [
                    new TextContent(" world!"),
                    new UsageContent(
                        new UsageDetails
                        {
                            InputTokenCount = 5,
                            OutputTokenCount = 2,
                            TotalTokenCount = 7,
                        }
                    ),
                ],
            },
        };

        using var innerClient = new TestChatClient { StreamingUpdatesToReturn = updates };

        using var client = new PostHogChatClient(
            innerClient,
            _mockPostHogClient.Object,
            _mockLogger.Object
        );

        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        // Act — consume the stream
        var received = new List<ChatResponseUpdate>();
        await foreach (
            var update in client.GetStreamingResponseAsync(
                messages,
                new ChatOptions { ModelId = "gpt-4" }
            )
        )
        {
            received.Add(update);
        }

        // Assert — all updates were yielded through
        Assert.Equal(2, received.Count);

        // Verify capture was called with accumulated data
        _mockPostHogClient.Verify(
            x =>
                x.Capture(
                    It.IsAny<string>(),
                    PostHogAIFieldNames.Generation,
                    It.Is<Dictionary<string, object>>(props =>
                        props.ContainsKey(PostHogAIFieldNames.Latency)
                        && (bool)props[PostHogAIFieldNames.Stream] == true
                    ),
                    null,
                    false,
                    It.IsAny<DateTimeOffset?>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task AppliesTraceContextFromPostHogAIContext()
    {
        // Arrange
        var expectedResponse = new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, "Hello!")]
        )
        {
            ModelId = "gpt-4",
        };

        using var innerClient = new TestChatClient { ResponseToReturn = expectedResponse };

        using var client = new PostHogChatClient(
            innerClient,
            _mockPostHogClient.Object,
            _mockLogger.Object
        );

        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        // Act
        using (
            PostHogAIContext.BeginScope(
                distinctId: "user-123",
                traceId: "trace-abc",
                spanId: "span-xyz",
                sessionId: "session-456"
            )
        )
        {
            await client.GetResponseAsync(messages);
        }

        // Assert — span ID should be unique (not the context's span ID),
        // and the context's span ID should become the parent ID
        _mockPostHogClient.Verify(
            x =>
                x.Capture(
                    "user-123",
                    PostHogAIFieldNames.Generation,
                    It.Is<Dictionary<string, object>>(props =>
                        (string)props[PostHogAIFieldNames.TraceId] == "trace-abc"
                        && (string)props[PostHogAIFieldNames.SpanId] != "span-xyz"
                        && (string)props[PostHogAIFieldNames.ParentId] == "span-xyz"
                        && (string)props[PostHogAIFieldNames.SessionId] == "session-456"
                    ),
                    null,
                    false,
                    It.IsAny<DateTimeOffset?>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task MultipleCallsInSameScopeGetUniqueSpanIds()
    {
        // Arrange
        var expectedResponse = new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, "Hello!")]
        )
        {
            ModelId = "gpt-4",
        };

        using var innerClient = new TestChatClient { ResponseToReturn = expectedResponse };

        using var client = new PostHogChatClient(
            innerClient,
            _mockPostHogClient.Object,
            _mockLogger.Object
        );

        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };
        var capturedSpanIds = new List<string>();

        _mockPostHogClient
            .Setup(x =>
                x.Capture(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<GroupCollection?>(),
                    It.IsAny<bool>(),
                    It.IsAny<DateTimeOffset?>()
                )
            )
            .Callback<string, string, Dictionary<string, object>, GroupCollection?, bool, DateTimeOffset?>(
                (_, _, props, _, _, _) =>
                {
                    if (props.TryGetValue(PostHogAIFieldNames.SpanId, out var spanId))
                    {
                        capturedSpanIds.Add((string)spanId);
                    }
                }
            );

        // Act — two calls within the same scope
        using (
            PostHogAIContext.BeginScope(
                distinctId: "user-123",
                traceId: "trace-abc",
                spanId: "scope-span"
            )
        )
        {
            await client.GetResponseAsync(messages);
            await client.GetResponseAsync(messages);
        }

        // Assert — two different span IDs, neither matching the scope span ID
        Assert.Equal(2, capturedSpanIds.Count);
        Assert.NotEqual(capturedSpanIds[0], capturedSpanIds[1]);
        Assert.DoesNotContain("scope-span", capturedSpanIds);
    }

    [Fact]
    public async Task NoPostHogContextGeneratesTraceId()
    {
        // Arrange
        var expectedResponse = new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, "Hello!")]
        )
        {
            ModelId = "gpt-4",
        };

        using var innerClient = new TestChatClient { ResponseToReturn = expectedResponse };

        using var client = new PostHogChatClient(
            innerClient,
            _mockPostHogClient.Object,
            _mockLogger.Object
        );

        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };

        // Act — no PostHogAIContext scope
        await client.GetResponseAsync(messages);

        // Assert — a traceId should still be generated
        _mockPostHogClient.Verify(
            x =>
                x.Capture(
                    It.IsAny<string>(),
                    PostHogAIFieldNames.Generation,
                    It.Is<Dictionary<string, object>>(props =>
                        props.ContainsKey(PostHogAIFieldNames.TraceId)
                        && !string.IsNullOrEmpty((string)props[PostHogAIFieldNames.TraceId])
                    ),
                    null,
                    false,
                    It.IsAny<DateTimeOffset?>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task PrivacyModeOmitsInputOutput()
    {
        // Arrange
        var expectedResponse = new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, "Secret response")]
        )
        {
            ModelId = "gpt-4",
            Usage = new UsageDetails
            {
                InputTokenCount = 5,
                OutputTokenCount = 3,
                TotalTokenCount = 8,
            },
        };

        using var innerClient = new TestChatClient { ResponseToReturn = expectedResponse };

        using var client = new PostHogChatClient(
            innerClient,
            _mockPostHogClient.Object,
            _mockLogger.Object
        );

        var messages = new List<ChatMessage> { new(ChatRole.User, "Secret prompt") };

        // Act
        using (PostHogAIContext.BeginScope(distinctId: "user-123", privacyMode: true))
        {
            await client.GetResponseAsync(messages);
        }

        // Assert — input and output should be omitted, but tokens should still be present
        _mockPostHogClient.Verify(
            x =>
                x.Capture(
                    "user-123",
                    PostHogAIFieldNames.Generation,
                    It.Is<Dictionary<string, object>>(props =>
                        !props.ContainsKey(PostHogAIFieldNames.Input)
                        && !props.ContainsKey(PostHogAIFieldNames.OutputChoices)
                        && (long)props[PostHogAIFieldNames.InputTokens] == 5
                        && (long)props[PostHogAIFieldNames.OutputTokens] == 3
                    ),
                    null,
                    false,
                    It.IsAny<DateTimeOffset?>()
                ),
            Times.Once
        );
    }

    /// <summary>
    /// Simple test implementation of IChatClient for unit testing.
    /// </summary>
    private sealed class TestChatClient : IChatClient
    {
        public ChatResponse? ResponseToReturn { get; set; }
        public List<ChatResponseUpdate>? StreamingUpdatesToReturn { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public int DelayMs { get; set; }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            if (DelayMs > 0)
            {
                await Task.Delay(DelayMs, cancellationToken);
            }

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            return ResponseToReturn ?? new ChatResponse([]);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            foreach (var update in StreamingUpdatesToReturn ?? [])
            {
                if (DelayMs > 0)
                {
                    await Task.Delay(DelayMs, cancellationToken);
                }

                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
