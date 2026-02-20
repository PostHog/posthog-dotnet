using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace PostHog.AI;

/// <summary>
/// A delegating handler that intercepts OpenAI API calls and sends events to PostHog.
/// </summary>
public class PostHogOpenAIHandler : DelegatingHandler
{
    private readonly IPostHogClient _postHogClient;
    private readonly ILogger<PostHogOpenAIHandler> _logger;

    public PostHogOpenAIHandler(IPostHogClient postHogClient, ILogger<PostHogOpenAIHandler> logger)
    {
        _postHogClient = postHogClient ?? throw new ArgumentNullException(nameof(postHogClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(request);
#else
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }
#endif

        // Capture context eagerly before any async work that might exit the caller's scope
        var capturedContext = PostHogAIContext.Current;

        var stopwatch = Stopwatch.StartNew();

        // Buffer request content so ReadAsStreamAsync doesn't consume the content before base.SendAsync
        if (request.Content != null)
        {
            await request.Content.LoadIntoBufferAsync();
        }

        var requestJson = await ReadContentAndParseJsonAsync(
            request.Content,
            ex => _logger.LogRequestContentFailure(ex),
            cancellationToken
        );

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            CaptureEvent(
                capturedContext,
                request,
                requestJson,
                null,
                null,
                stopwatch.Elapsed.TotalSeconds,
                DetermineEventName(request),
                ex
            );
            throw;
        }

        // Check for streaming
        var isStreaming = response.Content.Headers.ContentType?.MediaType == "text/event-stream";
        var eventName = DetermineEventName(request);

        if (isStreaming)
        {
            var privacyMode = capturedContext?.PrivacyMode == true;
#if NET8_0_OR_GREATER
            var originalStream = await response.Content.ReadAsStreamAsync(cancellationToken);
#else
            var originalStream = await response.Content.ReadAsStreamAsync();
#endif
            var trackingStream = new TrackingStream(
                originalStream,
                privacyMode,
                (accumulatedResponse, usage) =>
                {
                    stopwatch.Stop();
                    // Construct a pseudo-response JSON object from accumulated data
                    var responseNode = new JsonObject();

                    // Aggregate choices
                    var choicesArray = new JsonArray();
                    responseNode["choices"] = choicesArray;

                    if (!string.IsNullOrEmpty(accumulatedResponse))
                    {
                        var choice = new JsonObject();
                        var message = new JsonObject();
                        message["role"] = "assistant";
                        message["content"] = accumulatedResponse;
                        choice["message"] = message;
                        choicesArray.Add(choice);
                    }

                    if (usage != null)
                    {
                        responseNode["usage"] = usage;
                    }

                    CaptureEvent(
                        capturedContext,
                        request,
                        requestJson,
                        response,
                        responseNode,
                        stopwatch.Elapsed.TotalSeconds,
                        eventName,
                        null
                    );
                }
            );

            var newContent = new StreamContent(trackingStream);

            // Copy headers
            foreach (var header in response.Content.Headers)
            {
                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content = newContent;
        }
        else
        {
            stopwatch.Stop();
            var responseJson = await ReadContentAndParseJsonAsync(
                response.Content,
                _logger.LogResponseContentFailure,
                cancellationToken
            );

            CaptureEvent(
                capturedContext,
                request,
                requestJson,
                response,
                responseJson,
                stopwatch.Elapsed.TotalSeconds,
                eventName,
                null
            );
        }

        return response;
    }

    private static async Task<JsonNode?> ReadContentAndParseJsonAsync(
        HttpContent? content,
        Action<Exception> onException,
        CancellationToken cancellationToken
    )
    {
        JsonNode? jsonNode = null;

        try
        {
            if (content != null)
            {
                try
                {
#if NET8_0_OR_GREATER
                    var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
#else
                    var bytes = await content.ReadAsByteArrayAsync();
#endif
                    using var stream = new MemoryStream(bytes);
                    jsonNode = await JsonNode.ParseAsync(
                        stream,
                        cancellationToken: cancellationToken
                    );
                }
                catch (JsonException)
                {
                    // Ignore if not JSON
                }
            }
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            onException(ex);
        }
#pragma warning restore CA1031

        return jsonNode;
    }

    private static string DetermineEventName(HttpRequestMessage request)
    {
        if (
            request.RequestUri?.AbsolutePath.Contains(
                "/embeddings",
                StringComparison.OrdinalIgnoreCase
            ) == true
        )
        {
            return PostHogAIFieldNames.Embedding;
        }

        return PostHogAIFieldNames.Generation;
    }

    private void CaptureEvent(
        PostHogAIContext? context,
        HttpRequestMessage request,
        JsonNode? requestJson,
        HttpResponseMessage? response,
        JsonNode? responseJson,
        double latency,
        string eventName,
        Exception? exception
    )
    {
        try
        {
            var eventProperties = new Dictionary<string, object>();

            // Basic Info
            eventProperties[PostHogAIFieldNames.Provider] = "openai";
            eventProperties[PostHogAIFieldNames.Lib] = "posthog-dotnet";
            eventProperties[PostHogAIFieldNames.Latency] = latency;

            if (request.RequestUri != null)
            {
                eventProperties[PostHogAIFieldNames.BaseUrl] = request.RequestUri.GetLeftPart(
                    UriPartial.Authority
                );
                eventProperties[PostHogAIFieldNames.RequestUrl] = request.RequestUri.ToString();
            }

            // Request extraction
            string? model = null;
            if (requestJson != null)
            {
                model = requestJson["model"]?.ToString();
                eventProperties[PostHogAIFieldNames.Model] = model ?? "";

                var input = GetInputFromRequest(requestJson, context);
                if (input != null)
                {
                    eventProperties[PostHogAIFieldNames.Input] = input;
                }

                // Extract specific model parameters
                if (requestJson["temperature"] is JsonValue temperature)
                {
                    eventProperties[PostHogAIFieldNames.Temperature] =
                        temperature.TryGetValue<double>(out var temperatureDouble)
                            ? temperatureDouble
                            : temperature.ToString();
                }

                if (requestJson["max_tokens"] is JsonValue maxTokens)
                {
                    eventProperties[PostHogAIFieldNames.MaxTokens] = maxTokens.TryGetValue<int>(
                        out var maxTokensInt
                    )
                        ? maxTokensInt
                        : maxTokens.ToString();
                }

                if (requestJson["stream"] is JsonValue stream)
                {
                    eventProperties[PostHogAIFieldNames.Stream] = stream.TryGetValue<bool>(
                        out var streamBool
                    )
                        ? streamBool
                        : stream.ToString();
                }

                // Extract tools (check both "tools" and "functions" for compatibility)
                if (requestJson["tools"] is JsonArray tools)
                {
                    eventProperties[PostHogAIFieldNames.Tools] = tools;
                }
                else if (requestJson["functions"] is JsonArray functions)
                {
                    // Legacy support for functions parameter
                    eventProperties[PostHogAIFieldNames.Tools] = functions;
                }

                // Extract other parameters for model_parameters dictionary
                var modelParams = new Dictionary<string, object>();
                if (requestJson.AsObject() != null)
                {
                    foreach (var kvp in requestJson.AsObject())
                    {
                        if (
                            kvp.Key != "messages"
                            && kvp.Key != "prompt"
                            && kvp.Key != "input"
                            && kvp.Key != "model"
                            && kvp.Key != "temperature"
                            && kvp.Key != "max_tokens"
                            && kvp.Key != "stream"
                            && kvp.Key != "tools"
                            && kvp.Key != "functions"
                        )
                        {
                            // Simple types only for now
                            if (kvp.Value is JsonValue val)
                            {
                                modelParams[kvp.Key] = val.ToString();
                            }
                        }
                    }
                }

                if (modelParams.Count > 0)
                {
                    eventProperties[PostHogAIFieldNames.ModelParameters] = modelParams;
                }
            }

            // Detect streaming from response if not in request JSON
            if (
                !eventProperties.ContainsKey(PostHogAIFieldNames.Stream)
                && response?.Content.Headers.ContentType?.MediaType == "text/event-stream"
            )
            {
                eventProperties[PostHogAIFieldNames.Stream] = true;
            }

            // Response extraction
            if (response != null)
            {
                eventProperties[PostHogAIFieldNames.HttpStatus] = (int)response.StatusCode;
            }

            if (responseJson != null)
            {
                // Usage
                if (responseJson["usage"] is JsonObject usage)
                {
                    if (usage["prompt_tokens"] is JsonValue inputTokens)
                        eventProperties[PostHogAIFieldNames.InputTokens] =
                            inputTokens.GetValue<int>();

                    if (usage["completion_tokens"] is JsonValue outputTokens)
                        eventProperties[PostHogAIFieldNames.OutputTokens] =
                            outputTokens.GetValue<int>();

                    if (usage["total_tokens"] is JsonValue totalTokens)
                    {
                        eventProperties[PostHogAIFieldNames.TotalTokens] =
                            totalTokens.GetValue<int>();
                    }

                    // Cache properties (may not be present in OpenAI responses, but extract if available)
                    if (usage["cache_read_input_tokens"] is JsonValue cacheReadTokens)
                    {
                        if (cacheReadTokens.TryGetValue<int>(out var cacheReadInt))
                            eventProperties[PostHogAIFieldNames.CacheReadInputTokens] =
                                cacheReadInt;
                        else
                            eventProperties[PostHogAIFieldNames.CacheReadInputTokens] =
                                cacheReadTokens.ToString();
                    }

                    if (usage["cache_creation_input_tokens"] is JsonValue cacheCreationTokens)
                    {
                        if (cacheCreationTokens.TryGetValue<int>(out var cacheCreationInt))
                            eventProperties[PostHogAIFieldNames.CacheCreationInputTokens] =
                                cacheCreationInt;
                        else
                            eventProperties[PostHogAIFieldNames.CacheCreationInputTokens] =
                                cacheCreationTokens.ToString();
                    }
                }

                // Output choices (for generation)
                var outputChoices = GetOutputChoicesFromResponse(responseJson, eventName, context);
                if (outputChoices != null)
                {
                    eventProperties[PostHogAIFieldNames.OutputChoices] = outputChoices;
                }

                // Model from response might be more accurate
                if (responseJson["model"] != null)
                {
                    var responseModel = responseJson["model"]?.ToString();
                    if (!string.IsNullOrEmpty(responseModel))
                    {
                        eventProperties[PostHogAIFieldNames.Model] = responseModel;
                    }
                }
            }

            // Error handling
            if (exception != null || (response != null && !response.IsSuccessStatusCode))
            {
                eventProperties[PostHogAIFieldNames.IsError] = true;
                if (exception != null)
                {
                    eventProperties[PostHogAIFieldNames.Error] = exception.Message;
                }
                else if (responseJson != null && responseJson["error"] != null)
                {
                    eventProperties[PostHogAIFieldNames.Error] =
                        responseJson["error"]?.ToString() ?? "Unknown API Error";
                }
            }

            // Defaults if missing
            if (!eventProperties.ContainsKey(PostHogAIFieldNames.Model))
                eventProperties[PostHogAIFieldNames.Model] = model ?? "unknown";

            // Context integration
            var traceId = context?.TraceId ?? Guid.NewGuid().ToString();
            eventProperties[PostHogAIFieldNames.TraceId] = traceId;

            var distinctId = context?.DistinctId ?? traceId;

            // Add context-based properties (Properties dictionary takes precedence over context properties)
            // Session ID
            if (
                !eventProperties.ContainsKey(PostHogAIFieldNames.SessionId)
                && context?.SessionId != null
            )
            {
                eventProperties[PostHogAIFieldNames.SessionId] = context.SessionId;
            }

            // Span ID - generate unique per event
            if (!eventProperties.ContainsKey(PostHogAIFieldNames.SpanId))
            {
                eventProperties[PostHogAIFieldNames.SpanId] =
                    ActivitySpanId.CreateRandom().ToString();
            }

            // Span Name - use model
            if (!eventProperties.ContainsKey(PostHogAIFieldNames.SpanName))
            {
                var spanModel = eventProperties.TryGetValue(PostHogAIFieldNames.Model, out var sm)
                    ? sm?.ToString()
                    : null;
                eventProperties[PostHogAIFieldNames.SpanName] = spanModel ?? "chat-completion";
            }

            // Parent ID - context SpanId becomes parent
            if (!eventProperties.ContainsKey(PostHogAIFieldNames.ParentId))
            {
                var parentId = context?.SpanId ?? context?.ParentId;
                if (parentId != null)
                {
                    eventProperties[PostHogAIFieldNames.ParentId] = parentId;
                }
            }

            // Merge context properties (this will override context properties if they exist in Properties dict)
            // Skip null values to ensure optional fields like cost fields are not set unless explicitly provided
            if (context?.Properties != null)
            {
                foreach (var kvp in context.Properties)
                {
                    eventProperties[kvp.Key] = kvp.Value;
                }
            }

            GroupCollection? groups = null;
            if (context?.Groups != null)
            {
                groups = new GroupCollection();
                foreach (var kvp in context.Groups)
                {
                    groups.Add(kvp.Key, kvp.Value?.ToString() ?? string.Empty);
                }
            }
            // Create captured event
            _postHogClient.Capture(
                distinctId,
                eventName,
                eventProperties,
                groups,
                false, // sendFeatureFlags
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

    private static object? GetInputFromRequest(JsonNode? requestJson, PostHogAIContext? context)
    {
        if (requestJson == null)
        {
            return null;
        }

        // Don't extract input if privacy mode is enabled
        if (context?.PrivacyMode == true)
        {
            return null;
        }

        if (requestJson["messages"] is JsonArray messages)
        {
            return messages;
        }

        if (requestJson["prompt"] != null)
        {
            return requestJson["prompt"]?.ToString() ?? "";
        }

        if (requestJson["input"] != null) // For embeddings
        {
            var inputNode = requestJson["input"];
            if (inputNode is JsonArray arr)
            {
                return arr;
            }

            return inputNode?.ToString() ?? "";
        }

        return null;
    }

    private static JsonArray? GetOutputChoicesFromResponse(
        JsonNode? responseJson,
        string eventName,
        PostHogAIContext? context
    )
    {
        if (responseJson == null)
        {
            return null;
        }

        // Don't extract output choices if privacy mode is enabled
        if (context?.PrivacyMode == true)
        {
            return null;
        }

        if (
            eventName == PostHogAIFieldNames.Generation
            && responseJson["choices"] is JsonArray choices
        )
        {
            return choices;
        }

        return null;
    }

    private sealed class TrackingStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly bool _privacyMode;
        private readonly Action<string?, JsonNode?> _onComplete;
        private readonly StringBuilder? _accumulatedContent;
        private readonly StringBuilder _lineBuffer = new(capacity: 512);
        private JsonNode? _usage;
        private bool _completed;

        public TrackingStream(Stream innerStream, bool privacyMode, Action<string?, JsonNode?> onComplete)
        {
            _innerStream = innerStream;
            _privacyMode = privacyMode;
            _onComplete = onComplete;
            // Only allocate the content buffer when not in privacy mode
            _accumulatedContent = privacyMode ? null : new StringBuilder(capacity: 4 * 1024);
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;

        public override long Position
        {
            get => _innerStream.Position;
            set => _innerStream.Position = value;
        }

        public override void Flush() => _innerStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _innerStream.Read(buffer, offset, count);
            if (read > 0)
            {
                ProcessChunk(buffer, offset, read);
            }

            return read;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        )
        {
            // Delegate to the Memory<byte> overload to satisfy CA1835 and keep a single code path.
            var read = await ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
                .ConfigureAwait(false);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            var read = await _innerStream
                .ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read > 0)
            {
                if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
                {
                    ProcessChunk(segment.Array!, segment.Offset, read);
                }
                else
                {
                    // Fallback for non-array-backed Memory<byte>
                    var temp = buffer.Slice(0, read).ToArray();
                    ProcessChunk(temp, 0, read);
                }
            }

            return read;
        }

        private void ProcessChunk(byte[] buffer, int offset, int count)
        {
#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                var chunk = Encoding.UTF8.GetString(buffer, offset, count);

                // Append chunk to line buffer
                _lineBuffer.Append(chunk);

                // Convert to string once per ProcessChunk call to avoid O(n²) ToString() per iteration
                var bufferText = _lineBuffer.ToString();
                var startIndex = 0;

                while (startIndex < bufferText.Length)
                {
                    // Look for complete SSE message (terminated by \r\n\r\n or \n\n)
                    var messageEnd = bufferText.IndexOf("\r\n\r\n", startIndex, StringComparison.Ordinal);
                    var lineEndLength = 4;

                    if (messageEnd == -1)
                    {
                        messageEnd = bufferText.IndexOf("\n\n", startIndex, StringComparison.Ordinal);
                        if (messageEnd == -1)
                        {
                            // No complete message found, keep remaining buffer
                            break;
                        }
                        lineEndLength = 2;
                    }

                    // Extract complete message
                    var messageLength = messageEnd + lineEndLength - startIndex;
                    var message = bufferText.Substring(startIndex, messageLength);
                    startIndex = messageEnd + lineEndLength;

                    // Process the complete SSE message
                    ProcessSSEMessage(message);
                }

                // Rebuild buffer with only the unparsed tail
                _lineBuffer.Clear();
                if (startIndex < bufferText.Length)
                {
                    _lineBuffer.Append(bufferText, startIndex, bufferText.Length - startIndex);
                }
            }
            catch (Exception)
            {
                // Ignore other errors during chunk processing to avoid crashing the stream.
                // The stream should still flow to the client.
            }
#pragma warning restore CA1031
        }

        private void ProcessSSEMessage(string message)
        {
            // Enumerate lines using span-based enumeration to avoid allocations
#if NET8_0_OR_GREATER
            foreach (var line in message.AsSpan().EnumerateLines())
            {
                ProcessLine(line);
            }
#else
            // Simpler but less efficient: split by newlines
            var lines = message.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                ProcessLine(line.AsSpan());
            }
#endif
        }

        private void ProcessLine(ReadOnlySpan<char> line)
        {
            var trimmed = line.Trim();
            if (trimmed.IsEmpty)
                return;

            if (trimmed.StartsWith("data: ".AsSpan(), StringComparison.Ordinal))
            {
                var dataSpan = trimmed.Slice(6).Trim();
                if (dataSpan.IsEmpty || dataSpan.SequenceEqual("[DONE]".AsSpan()))
                    return;

                // Convert to string only when needed for JSON parsing
                var data = dataSpan.ToString();
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    // Check for usage (usually in the last chunk)
                    if (root.TryGetProperty("usage", out var usageElement) && _usage == null)
                    {
                        // Extract usage into a JsonNode by re-parsing the raw text
                        _usage = JsonNode.Parse(usageElement.GetRawText());
                    }

                    // Check for delta content (skip in privacy mode)
                    if (
                        _accumulatedContent != null
                        && root.TryGetProperty("choices", out var choicesElement)
                        && choicesElement.ValueKind == JsonValueKind.Array
                        && choicesElement.GetArrayLength() > 0
                    )
                    {
                        var firstChoice = choicesElement[0];
                        if (
                            firstChoice.TryGetProperty("delta", out var deltaElement)
                            && deltaElement.TryGetProperty("content", out var contentElement)
                            && contentElement.ValueKind == JsonValueKind.String
                        )
                        {
                            var content = contentElement.GetString();
                            if (!string.IsNullOrEmpty(content))
                            {
                                _accumulatedContent.Append(content);
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Ignore parsing errors for malformed JSON, this can happen in streaming.
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_completed)
            {
                _completed = true;
                _onComplete(_accumulatedContent?.ToString(), _usage);
            }

            if (disposing)
            {
                _innerStream.Dispose();
            }
            base.Dispose(disposing);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            _innerStream.Seek(offset, origin);

        public override void SetLength(long value) => _innerStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            _innerStream.Write(buffer, offset, count);
    }
}

internal static partial class PostHogAILoggerExtensions
{
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Failed to read request content for PostHog AI capture."
    )]
    public static partial void LogRequestContentFailure(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Failed to read response content for PostHog AI capture."
    )]
    public static partial void LogResponseContentFailure(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Error capturing PostHog AI event."
    )]
    public static partial void LogCaptureFailure(this ILogger logger, Exception ex);
}
