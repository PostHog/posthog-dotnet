#if NET8_0_OR_GREATER
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace PostHog.AI;

/// <summary>
/// A <see cref="DelegatingChatClient"/> that intercepts AI chat operations
/// and sends <c>$ai_generation</c> events to PostHog.
/// </summary>
public class PostHogChatClient : DelegatingChatClient
{
    private readonly IPostHogClient _postHogClient;
    private readonly ILogger<PostHogChatClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostHogChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner <see cref="IChatClient"/> to delegate to.</param>
    /// <param name="postHogClient">The PostHog client for capturing events.</param>
    /// <param name="logger">The logger.</param>
    public PostHogChatClient(
        IChatClient innerClient,
        IPostHogClient postHogClient,
        ILogger<PostHogChatClient> logger
    )
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(postHogClient);
        ArgumentNullException.ThrowIfNull(logger);
        _postHogClient = postHogClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var context = PostHogAIContext.Current;

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            CaptureEvent(
                messages,
                options,
                response: null,
                stopwatch.Elapsed.TotalSeconds,
                ex,
                isStreaming: false,
                context: context
            );
            throw;
        }

        stopwatch.Stop();
        CaptureEvent(
            messages,
            options,
            response,
            stopwatch.Elapsed.TotalSeconds,
            exception: null,
            isStreaming: false,
            context: context
        );
        return response;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var stopwatch = Stopwatch.StartNew();
        // Capture context early since it's AsyncLocal and may not be available when enumeration completes
        var context = PostHogAIContext.Current;
        var updates = new List<ChatResponseUpdate>();

        await foreach (
            var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
        )
        {
            updates.Add(update);
            yield return update;
        }

        stopwatch.Stop();

        try
        {
            var response = updates.Count > 0 ? updates.ToChatResponse() : null;
            CaptureEvent(
                messages,
                options,
                response,
                stopwatch.Elapsed.TotalSeconds,
                exception: null,
                isStreaming: true,
                context: context
            );
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            _logger.LogCaptureFailure(ex);
        }
#pragma warning restore CA1031
    }

    private void CaptureEvent(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        ChatResponse? response,
        double latencySeconds,
        Exception? exception,
        bool isStreaming,
        PostHogAIContext? context
    )
    {
        try
        {
            var eventProperties = new Dictionary<string, object>();

            // Basic info
            eventProperties[PostHogAIFieldNames.Provider] = "openai";
            eventProperties[PostHogAIFieldNames.Lib] = "posthog-dotnet";
            eventProperties[PostHogAIFieldNames.Latency] = latencySeconds;

            // Model — response model takes precedence over options model
            var model = response?.ModelId ?? options?.ModelId;
            eventProperties[PostHogAIFieldNames.Model] = model ?? "unknown";

            // Request parameters
            if (options?.Temperature is { } temperature)
            {
                eventProperties[PostHogAIFieldNames.Temperature] = temperature;
            }

            if (options?.MaxOutputTokens is { } maxTokens)
            {
                eventProperties[PostHogAIFieldNames.MaxTokens] = maxTokens;
            }

            // Streaming flag
            if (isStreaming)
            {
                eventProperties[PostHogAIFieldNames.Stream] = true;
            }

            // Input messages (respect privacy mode)
            if (context?.PrivacyMode != true)
            {
                eventProperties[PostHogAIFieldNames.Input] = SerializeMessages(messages);
            }

            // Response: output choices and usage
            if (response != null)
            {
                // Output choices (respect privacy mode)
                if (context?.PrivacyMode != true && response.Messages.Count > 0)
                {
                    eventProperties[PostHogAIFieldNames.OutputChoices] = SerializeOutputMessages(
                        response.Messages
                    );
                }

                // Usage
                if (response.Usage != null)
                {
                    if (response.Usage.InputTokenCount is { } inputTokens)
                    {
                        eventProperties[PostHogAIFieldNames.InputTokens] = inputTokens;
                    }

                    if (response.Usage.OutputTokenCount is { } outputTokens)
                    {
                        eventProperties[PostHogAIFieldNames.OutputTokens] = outputTokens;
                    }

                    if (response.Usage.TotalTokenCount is { } totalTokens)
                    {
                        eventProperties[PostHogAIFieldNames.TotalTokens] = totalTokens;
                    }
                }
            }

            // Error handling
            if (exception != null)
            {
                eventProperties[PostHogAIFieldNames.IsError] = true;
                eventProperties[PostHogAIFieldNames.Error] = exception.Message;
            }

            // Context integration
            var traceId = context?.TraceId ?? Guid.NewGuid().ToString();
            eventProperties[PostHogAIFieldNames.TraceId] = traceId;

            var distinctId = context?.DistinctId ?? traceId;

            if (context?.SessionId != null)
            {
                eventProperties[PostHogAIFieldNames.SessionId] = context.SessionId;
            }

            // Generate a unique span ID per generation event
            eventProperties[PostHogAIFieldNames.SpanId] = ActivitySpanId.CreateRandom().ToString();

            // Use model as span name for individual generations
            eventProperties[PostHogAIFieldNames.SpanName] = model ?? "chat-completion";

            // Context's SpanId becomes this event's parent, linking back to the scope
            var parentId = context?.SpanId ?? context?.ParentId;
            if (parentId != null)
            {
                eventProperties[PostHogAIFieldNames.ParentId] = parentId;
            }

            // Merge context properties
            if (context?.Properties != null)
            {
                foreach (var kvp in context.Properties)
                {
                    eventProperties[kvp.Key] = kvp.Value;
                }
            }

            // Groups
            GroupCollection? groups = null;
            if (context?.Groups != null)
            {
                groups = new GroupCollection();
                foreach (var kvp in context.Groups)
                {
                    groups.Add(kvp.Key, kvp.Value?.ToString() ?? string.Empty);
                }
            }

            _postHogClient.Capture(
                distinctId,
                PostHogAIFieldNames.Generation,
                eventProperties,
                groups,
                false,
                DateTimeOffset.UtcNow
            );
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            _logger.LogCaptureFailure(ex);
        }
#pragma warning restore CA1031
    }

    private static List<Dictionary<string, object>> SerializeMessages(
        IEnumerable<ChatMessage> messages
    )
    {
        var result = new List<Dictionary<string, object>>();
        foreach (var msg in messages)
        {
            result.Add(
                new Dictionary<string, object>
                {
                    ["role"] = msg.Role.Value,
                    ["content"] = msg.Text ?? string.Empty,
                }
            );
        }

        return result;
    }

    private static List<Dictionary<string, object>> SerializeOutputMessages(
        IEnumerable<ChatMessage> messages
    )
    {
        var result = new List<Dictionary<string, object>>();
        foreach (var msg in messages)
        {
            result.Add(
                new Dictionary<string, object>
                {
                    ["message"] = new Dictionary<string, object>
                    {
                        ["role"] = msg.Role.Value,
                        ["content"] = msg.Text ?? string.Empty,
                    },
                }
            );
        }

        return result;
    }
}
#endif
