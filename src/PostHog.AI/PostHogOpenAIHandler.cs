using System.Diagnostics;
using System.Runtime.InteropServices; // Added for MemoryMarshal
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
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var (requestContent, requestJson) = await ReadContentAndParseJsonAsync(
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
            // Capture error event
            _ = Task.Run(
                () =>
                    CaptureEventAsync(
                        request,
                        requestJson,
                        null,
                        null,
                        stopwatch.Elapsed.TotalSeconds,
                        DetermineEventName(request),
                        ex,
                        CancellationToken.None
                    ),
                CancellationToken.None
            );
            throw;
        }

        // Check for streaming
        var isStreaming = response.Content?.Headers.ContentType?.MediaType == "text/event-stream";
        var eventName = DetermineEventName(request);

        if (isStreaming && response.Content != null)
        {
            var originalStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var trackingStream = new TrackingStream(
                originalStream,
                (accumulatedResponse, usageNode) =>
                {
                    stopwatch.Stop();
                    // Construct a pseudo-response JSON object from accumulated data
                    var responseNode = new JsonObject();

                    // Aggregate choices
                    var choicesArray = new JsonArray();
                    responseNode["choices"] = choicesArray;

                    // If we have content, create a choice.
                    // Note: This is a simplification. In reality, we might have multiple choices/tool calls.
                    // For valid AI Generation events, we'd ideally want to reconstruct the full structure.
                    // But for now, we'll try to at least capture the text.
                    if (!string.IsNullOrEmpty(accumulatedResponse))
                    {
                        var choice = new JsonObject();
                        var message = new JsonObject();
                        message["role"] = "assistant";
                        message["content"] = accumulatedResponse;
                        choice["message"] = message;
                        choicesArray.Add(choice);
                    }

                    if (usageNode != null)
                    {
                        responseNode["usage"] = usageNode;
                    }

                    _ = Task.Run(
                        () =>
                            CaptureEventAsync(
                                request,
                                requestJson,
                                response,
                                responseNode,
                                stopwatch.Elapsed.TotalSeconds,
                                eventName,
                                null,
                                CancellationToken.None
                            ),
                        CancellationToken.None
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
            var (responseContent, responseJson) = await ReadContentAndParseJsonAsync(
                response.Content,
                ex => _logger.LogResponseContentFailure(ex),
                cancellationToken
            );

            _ = Task.Run(
                () =>
                    CaptureEventAsync(
                        request,
                        requestJson,
                        response,
                        responseJson,
                        stopwatch.Elapsed.TotalSeconds,
                        eventName,
                        null,
                        CancellationToken.None
                    ),
                CancellationToken.None
            );
        }

        return response;
    }

    private static async Task<(string? Content, JsonNode? Json)> ReadContentAndParseJsonAsync(
        HttpContent? content,
        Action<Exception> onException,
        CancellationToken cancellationToken
    )
    {
        string? contentString = null;
        JsonNode? jsonNode = null;

        try
        {
            if (content != null)
            {
#pragma warning disable CA2016 // Forward cancellation token (ReadAsStringAsync has an overload with CancellationToken in .NET 5+, LoadIntoBufferAsync doesn't)
                await content.LoadIntoBufferAsync();
                contentString = await content.ReadAsStringAsync(cancellationToken);
#pragma warning restore CA2016

                if (!string.IsNullOrWhiteSpace(contentString))
                {
                    try
                    {
                        jsonNode = JsonNode.Parse(contentString);
                    }
                    catch (JsonException)
                    {
                        // Ignore if not JSON
                    }
                }
            }
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            onException(ex);
        }
#pragma warning restore CA1031

        return (contentString, jsonNode);
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
            return "$ai_embedding";
        }

        return "$ai_generation";
    }

    private Task CaptureEventAsync(
        HttpRequestMessage request,
        JsonNode? requestJson,
        HttpResponseMessage? response,
        JsonNode? responseJson,
        double latency,
        string eventName,
        Exception? exception,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var eventProperties = new Dictionary<string, object>();

            // Basic Info
            eventProperties["$ai_provider"] = "openai";
            eventProperties["$ai_lib"] = "posthog-dotnet";
            eventProperties["$ai_latency"] = latency;

            if (request.RequestUri != null)
            {
                eventProperties["$ai_base_url"] = request.RequestUri.GetLeftPart(
                    UriPartial.Authority
                );
                eventProperties["$ai_request_url"] = request.RequestUri.ToString();
            }

            // Request extraction
            string? model = null;
            if (requestJson != null)
            {
                model = requestJson["model"]?.ToString();
                eventProperties["$ai_model"] = model ?? "";

                // Extract input
                if (requestJson["messages"] is JsonArray messages)
                {
                    eventProperties["$ai_input"] = messages;
                }
                else if (requestJson["prompt"] != null)
                {
                    eventProperties["$ai_input"] = requestJson["prompt"]?.ToString() ?? "";
                }
                else if (requestJson["input"] != null) // For embeddings
                {
                    var inputNode = requestJson["input"];
                    if (inputNode is JsonArray arr)
                        eventProperties["$ai_input"] = arr;
                    else
                        eventProperties["$ai_input"] = inputNode?.ToString() ?? "";
                }

                // Extract specific model parameters
                if (requestJson["temperature"] is JsonValue temperature)
                {
                    if (temperature.TryGetValue<double>(out var temperatureDouble))
                        eventProperties["$ai_temperature"] = temperatureDouble;
                    else
                        eventProperties["$ai_temperature"] = temperature.ToString();
                }

                if (requestJson["max_tokens"] is JsonValue maxTokens)
                {
                    if (maxTokens.TryGetValue<int>(out var maxTokensInt))
                        eventProperties["$ai_max_tokens"] = maxTokensInt;
                    else
                        eventProperties["$ai_max_tokens"] = maxTokens.ToString();
                }

                if (requestJson["stream"] is JsonValue stream)
                {
                    if (stream.TryGetValue<bool>(out var streamBool))
                        eventProperties["$ai_stream"] = streamBool;
                    else
                        eventProperties["$ai_stream"] = stream.ToString();
                }

                // Extract tools (check both "tools" and "functions" for compatibility)
                if (requestJson["tools"] is JsonArray tools)
                {
                    eventProperties["$ai_tools"] = tools;
                }
                else if (requestJson["functions"] is JsonArray functions)
                {
                    // Legacy support for functions parameter
                    eventProperties["$ai_tools"] = functions;
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
                    eventProperties["$ai_model_parameters"] = modelParams;
                }
            }

            // Detect streaming from response if not in request JSON
            if (
                !eventProperties.ContainsKey("$ai_stream")
                && response?.Content?.Headers.ContentType?.MediaType == "text/event-stream"
            )
            {
                eventProperties["$ai_stream"] = true;
            }

            // Response extraction
            if (response != null)
            {
                eventProperties["$ai_http_status"] = (int)response.StatusCode;
            }

            if (responseJson != null)
            {
                // Usage
                if (responseJson["usage"] is JsonObject usage)
                {
                    if (usage["prompt_tokens"] is JsonValue inputTokens)
                        eventProperties["$ai_input_tokens"] = inputTokens.GetValue<int>();

                    if (usage["completion_tokens"] is JsonValue outputTokens)
                        eventProperties["$ai_output_tokens"] = outputTokens.GetValue<int>();

                    if (usage["total_tokens"] is JsonValue totalTokens)
                    {
                        // Optional but good to have
                        eventProperties["$ai_total_tokens"] = totalTokens.GetValue<int>();
                    }

                    // Cache properties (may not be present in OpenAI responses, but extract if available)
                    if (usage["cache_read_input_tokens"] is JsonValue cacheReadTokens)
                    {
                        if (cacheReadTokens.TryGetValue<int>(out var cacheReadInt))
                            eventProperties["$ai_cache_read_input_tokens"] = cacheReadInt;
                        else
                            eventProperties["$ai_cache_read_input_tokens"] =
                                cacheReadTokens.ToString();
                    }

                    if (usage["cache_creation_input_tokens"] is JsonValue cacheCreationTokens)
                    {
                        if (cacheCreationTokens.TryGetValue<int>(out var cacheCreationInt))
                            eventProperties["$ai_cache_creation_input_tokens"] = cacheCreationInt;
                        else
                            eventProperties["$ai_cache_creation_input_tokens"] =
                                cacheCreationTokens.ToString();
                    }
                }

                // Output choices (for generation)
                if (eventName == "$ai_generation" && responseJson["choices"] is JsonArray choices)
                {
                    eventProperties["$ai_output_choices"] = choices;
                }

                // Model from response might be more accurate
                if (responseJson["model"] != null)
                {
                    var responseModel = responseJson["model"]?.ToString();
                    if (!string.IsNullOrEmpty(responseModel))
                    {
                        eventProperties["$ai_model"] = responseModel;
                    }
                }
            }

            // Error handling
            if (exception != null || (response != null && !response.IsSuccessStatusCode))
            {
                eventProperties["$ai_is_error"] = true;
                if (exception != null)
                {
                    eventProperties["$ai_error"] = exception.Message;
                }
                else if (responseJson != null && responseJson["error"] != null)
                {
                    eventProperties["$ai_error"] =
                        responseJson["error"]?.ToString() ?? "Unknown API Error";
                }
            }

            // Defaults if missing
            if (!eventProperties.ContainsKey("$ai_model"))
                eventProperties["$ai_model"] = model ?? "unknown";
            eventProperties["$ai_request_url"] ??= request.RequestUri?.ToString() ?? "unknown";

            // Context integration
            var context = PostHogAIContext.Current;
            var traceId = context?.TraceId ?? Guid.NewGuid().ToString(); // Use context traceId or generate new
            eventProperties["$ai_trace_id"] = traceId; // Ensure we update the property

            var distinctId = context?.DistinctId ?? traceId;

            // Add context-based properties (Properties dictionary takes precedence over context properties)
            // Session ID
            if (!eventProperties.ContainsKey("$ai_session_id") && context?.SessionId != null)
            {
                eventProperties["$ai_session_id"] = context.SessionId;
            }

            // Span ID
            if (!eventProperties.ContainsKey("$ai_span_id") && context?.SpanId != null)
            {
                eventProperties["$ai_span_id"] = context.SpanId;
            }

            // Span Name
            if (!eventProperties.ContainsKey("$ai_span_name") && context?.SpanName != null)
            {
                eventProperties["$ai_span_name"] = context.SpanName;
            }

            // Parent ID
            if (!eventProperties.ContainsKey("$ai_parent_id") && context?.ParentId != null)
            {
                eventProperties["$ai_parent_id"] = context.ParentId;
            }

            // Merge context properties (this will override context properties if they exist in Properties dict)
            // Skip null values to ensure optional fields like cost fields are not set unless explicitly provided
            if (context?.Properties != null)
            {
                foreach (var kvp in context.Properties)
                {
                    // Only set property if value is not null
                    // This ensures cost fields ($ai_input_cost_usd, etc.) are not set unless explicitly provided
                    if (kvp.Value != null)
                    {
                        eventProperties[kvp.Key] = kvp.Value;
                    }
                }
            }

            GroupCollection? groups = null;
            if (context?.Groups != null)
            {
                groups = new GroupCollection();
                foreach (var kvp in context.Groups)
                {
                    if (kvp.Value != null)
                    {
                        groups.Add(kvp.Key, kvp.Value.ToString() ?? "");
                    }
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

        return Task.CompletedTask;
#pragma warning restore CA1031
    }

    private sealed class TrackingStream : Stream
    {
        private static readonly string[] LineSeparators = { "\r\n", "\n" };

        private readonly Stream _innerStream;
        private readonly Action<string, JsonNode?> _onComplete;
        private readonly StringBuilder _accumulatedContent = new();
        private readonly StringBuilder _lineBuffer = new(); // Buffer for incomplete lines across chunks
        private JsonNode? _usage;
        private bool _completed;

        public TrackingStream(Stream innerStream, Action<string, JsonNode?> onComplete)
        {
            _innerStream = innerStream;
            _onComplete = onComplete;
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

                // Process complete SSE messages (terminated by \n\n or \r\n\r\n)
                while (true)
                {
                    var bufferText = _lineBuffer.ToString();
                    if (string.IsNullOrEmpty(bufferText))
                        break;

                    // Look for complete SSE message (terminated by \n\n or \r\n\r\n)
                    var messageEnd = bufferText.IndexOf("\n\n", StringComparison.Ordinal);
                    var lineEndLength = 2;

                    if (messageEnd == -1)
                    {
                        messageEnd = bufferText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (messageEnd == -1)
                        {
                            // No complete message found, keep remaining buffer
                            break;
                        }
                        lineEndLength = 4;
                    }

                    // Extract complete message (including the line ending)
                    var messageLength = messageEnd + lineEndLength;
                    var message = bufferText.Substring(0, messageLength);

                    // Remove processed message from buffer
                    _lineBuffer.Remove(0, messageLength);

                    // Process the complete SSE message
                    ProcessSSEMessage(message);
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
            // Split message into lines
            var lines = message.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("data: ", StringComparison.Ordinal))
                {
                    var data = trimmed.Substring(6).Trim();
                    if (string.IsNullOrEmpty(data) || data == "[DONE]")
                        continue;

                    try
                    {
                        var node = JsonNode.Parse(data);
                        if (node != null)
                        {
                            // Check for usage (usually in the last chunk, but can appear earlier for some models)
                            if (node["usage"] != null && _usage == null) // Only capture first usage if multiple sent
                            {
                                _usage = node["usage"]?.DeepClone();
                            }

                            // Check for delta content
                            if (node["choices"] is JsonArray choices && choices.Count > 0)
                            {
                                if (choices[0] is JsonObject firstChoiceObject)
                                {
                                    if (
                                        firstChoiceObject.TryGetPropertyValue(
                                            "delta",
                                            out var deltaNode
                                        )
                                    )
                                    {
                                        if (deltaNode is JsonObject deltaObject)
                                        {
                                            if (
                                                deltaObject.TryGetPropertyValue(
                                                    "content",
                                                    out var contentNode
                                                )
                                            )
                                            {
                                                if (contentNode is JsonValue contentValue)
                                                {
                                                    var content = contentValue.ToString();
                                                    if (!string.IsNullOrEmpty(content))
                                                    {
                                                        _accumulatedContent.Append(content);
                                                    }
                                                }
                                            }
                                        }
                                    }
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_completed)
            {
                _completed = true;
                _onComplete(_accumulatedContent.ToString(), _usage);
            }

            base.Dispose(disposing);
            _innerStream.Dispose();
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
