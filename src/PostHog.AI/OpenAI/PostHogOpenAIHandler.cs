using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable CA1848, CA1031, CA1307, CA1310, CA1822, CA2016

namespace PostHog.AI.OpenAI;

public class PostHogOpenAIHandler : DelegatingHandler
{
    private readonly IPostHogClient _postHogClient;
    private readonly ILogger<PostHogOpenAIHandler> _logger;
    private readonly PostHogAIOptions _options;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public PostHogOpenAIHandler(
        IPostHogClient postHogClient,
        ILogger<PostHogOpenAIHandler> logger,
        IOptions<PostHogAIOptions> options
    )
    {
#if NETSTANDARD2_1
        _postHogClient = postHogClient ?? throw new ArgumentNullException(nameof(postHogClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
#else
        ArgumentNullException.ThrowIfNull(postHogClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Value == null)
            throw new ArgumentNullException(nameof(options));
        _postHogClient = postHogClient;
        _logger = logger;
        _options = options.Value;
#endif
        _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
#if NETSTANDARD2_1
        if (request == null)
            throw new ArgumentNullException(nameof(request));
#else
        ArgumentNullException.ThrowIfNull(request);
#endif
        // Check if this is an OpenAI API request
        if (!IsOpenAIRequest(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var startTime = DateTimeOffset.UtcNow;
        string? requestBody = null;
        Dictionary<string, object>? requestJson = null;

        // Read and buffer the request content
        if (request.Content != null)
        {
#if NETSTANDARD2_1
            requestBody = await request.Content.ReadAsStringAsync();
#else
            requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
#endif
            // Restore the request content since we consumed it
            request.Content = new StringContent(requestBody);

            // Parse request body for PostHog parameters
            if (!string.IsNullOrEmpty(requestBody))
            {
                try
                {
                    requestJson = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        requestBody,
                        _jsonSerializerOptions
                    );
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug(ex, "Failed to parse OpenAI request body as JSON");
                }
            }
        }

        try
        {
            var response = await base.SendAsync(request, cancellationToken);

            // Determine if this is a streaming response
            var isStreaming =
                response.Content.Headers.ContentType?.MediaType == "text/event-stream"
                || (
                    requestJson?.ContainsKey("stream") == true
                    && requestJson["stream"] is JsonElement streamElement
                    && streamElement.ValueKind == JsonValueKind.True
                );

            if (isStreaming)
            {
                // For streaming responses, buffer the entire content and process synchronously
                // This ensures monitoring happens and the response is preserved
                if (response.Content != null)
                {
#if NETSTANDARD2_1
                    using var stream = await response.Content.ReadAsStreamAsync();
#else
                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
#endif
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    var buffer = memoryStream.ToArray();
                    
                    // Create new stream for the response
                    response.Content = new StreamContent(new MemoryStream(buffer));
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                    
                    // Parse the buffered stream for monitoring
                    var streamText = Encoding.UTF8.GetString(buffer);
                    await ProcessStreamingTextAsync(
                        request,
                        requestBody,
                        requestJson,
                        streamText,
                        startTime,
                        response.StatusCode,
                        cancellationToken
                    );
                }
                else
                {
                    // No content, process asynchronously
                    _ = Task.Run(
                        () =>
                            ProcessRequestAsync(
                                request,
                                requestBody,
                                requestJson,
                                response,
                                startTime,
                                cancellationToken
                            ),
                        CancellationToken.None // Don't cancel monitoring
                    );
                }

                return response;
            }
            else
            {
                // For non-streaming responses, process synchronously to ensure content is available
                // Buffer the response content first
                string? responseBody = null;
                if (response.Content != null)
                {
#if NETSTANDARD2_1
                    responseBody = await response.Content.ReadAsStringAsync();
#else
                    responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
#endif
                    // Restore the response content since we consumed it
                    response.Content = new StringContent(responseBody, Encoding.UTF8, response.Content.Headers.ContentType?.MediaType ?? "application/json");
                }

                await ProcessRequestAsync(
                    request,
                    requestBody,
                    requestJson,
                    response,
                    startTime,
                    responseBody,
                    cancellationToken
                );

                return response;
            }
        }
        catch (Exception ex)
        {
            // If request fails, still try to capture the error
            _ = Task.Run(
                () =>
                    ProcessErrorAsync(
                        request,
                        requestBody,
                        requestJson,
                        ex,
                        startTime,
                        cancellationToken
                    ),
                cancellationToken
            );
            throw;
        }
    }

    private static bool IsOpenAIRequest(HttpRequestMessage request)
    {
        var uri = request.RequestUri?.ToString() ?? "";
        return uri.Contains("openai.azure.com", StringComparison.Ordinal)
            || uri.Contains("api.openai.com", StringComparison.Ordinal)
            || uri.Contains(".openai.azure.com", StringComparison.Ordinal);
    }

    private async Task ProcessRequestAsync(
        HttpRequestMessage request,
        string? requestBody,
        Dictionary<string, object>? requestJson,
        HttpResponseMessage response,
        DateTimeOffset startTime,
        CancellationToken cancellationToken
    )
    {
        await ProcessRequestAsync(request, requestBody, requestJson, response, startTime, responseBody: null, cancellationToken);
    }

    private async Task ProcessRequestAsync(
        HttpRequestMessage request,
        string? requestBody,
        Dictionary<string, object>? requestJson,
        HttpResponseMessage response,
        DateTimeOffset startTime,
        string? responseBody,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var latency = (DateTimeOffset.UtcNow - startTime).TotalSeconds;

            // Extract PostHog parameters from request JSON
            var posthogParams = ExtractPosthogParams(requestJson);

            // Determine if this is a streaming response
            var isStreaming =
                response.Content.Headers.ContentType?.MediaType == "text/event-stream"
                || (
                    requestJson?.ContainsKey("stream") == true
                    && requestJson["stream"] is JsonElement streamElement
                    && streamElement.ValueKind == JsonValueKind.True
                );

            OpenAIResponseData? responseData = null;

            if (isStreaming)
            {
                responseData = await ProcessStreamingResponseAsync(response, cancellationToken);
            }
            else
            {
                string? responseBodyToParse = responseBody;
                if (responseBodyToParse == null && response.Content != null)
                {
#if NETSTANDARD2_1
                    responseBodyToParse = await response.Content.ReadAsStringAsync();
#else
                    responseBodyToParse = await response.Content.ReadAsStringAsync(cancellationToken);
#endif
                }
                responseData = ParseOpenAIResponse(responseBodyToParse ?? string.Empty, response.StatusCode);
            }

            // Parse request data
            var requestData = ParseOpenAIRequest(request, requestBody, requestJson);

            // Determine event type based on endpoint
            var eventType = GetAIEventType(request);

            // Send event to PostHog
            await SendAIEventAsync(
                eventType,
                requestData,
                responseData,
                posthogParams,
                latency,
                response.StatusCode,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OpenAI request for PostHog");
        }
    }

    private async Task ProcessErrorAsync(
        HttpRequestMessage request,
        string? requestBody,
        Dictionary<string, object>? requestJson,
        Exception exception,
        DateTimeOffset startTime,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var latency = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
            var posthogParams = ExtractPosthogParams(requestJson);
            var requestData = ParseOpenAIRequest(request, requestBody, requestJson);
            var eventType = GetAIEventType(request);

            // Send error event to PostHog
            await SendAIEventAsync(
                eventType,
                requestData,
                null,
                posthogParams,
                latency,
                HttpStatusCode.InternalServerError,
                cancellationToken,
                exception
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OpenAI error for PostHog");
        }
    }

    private PosthogParams ExtractPosthogParams(Dictionary<string, object>? requestJson)
    {
        var paramsDict = new PosthogParams();

        if (requestJson == null)
            return paramsDict;

        // Map of PostHog parameter names from request JSON to our params object
        // Following JavaScript package pattern: posthogDistinctId -> DistinctId, etc.
        var paramMappings = new Dictionary<string, Action<object>>
        {
            ["posthogDistinctId"] = v => paramsDict.DistinctId = v?.ToString(),
            ["posthogTraceId"] = v =>
                paramsDict.TraceId = v?.ToString() ?? Guid.NewGuid().ToString(),
            ["posthogPrivacyMode"] = v =>
                paramsDict.PrivacyMode = v is JsonElement je && je.ValueKind == JsonValueKind.True,
            ["posthogModelOverride"] = v => paramsDict.ModelOverride = v?.ToString(),
            ["posthogProviderOverride"] = v => paramsDict.ProviderOverride = v?.ToString(),
            ["posthogCaptureImmediate"] = v =>
                paramsDict.CaptureImmediate =
                    v is JsonElement je2 && je2.ValueKind == JsonValueKind.True,
        };

        foreach (var kvp in requestJson)
        {
            if (paramMappings.TryGetValue(kvp.Key, out var setter))
            {
                try
                {
                    setter(kvp.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to set PostHog parameter {Parameter}", kvp.Key);
                }
            }
            else if (kvp.Key.StartsWith("posthog", StringComparison.Ordinal))
            {
                _logger.LogDebug("Unknown PostHog parameter {Parameter} in request", kvp.Key);
            }
        }

        // Extract properties and groups if they exist as JSON objects
        if (
            requestJson.TryGetValue("posthogProperties", out var propertiesValue)
            && propertiesValue is JsonElement propertiesElement
        )
        {
            try
            {
                paramsDict.Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    propertiesElement.GetRawText(),
                    _jsonSerializerOptions
                );
            }
            catch (JsonException) { }
        }

        if (
            requestJson.TryGetValue("posthogGroups", out var groupsValue)
            && groupsValue is JsonElement groupsElement
        )
        {
            try
            {
                paramsDict.Groups = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    groupsElement.GetRawText(),
                    _jsonSerializerOptions
                );
            }
            catch (JsonException) { }
        }

        // Set defaults
        if (string.IsNullOrEmpty(paramsDict.TraceId))
        {
            paramsDict.TraceId = Guid.NewGuid().ToString();
        }

        paramsDict.PrivacyMode = paramsDict.PrivacyMode || _options.PrivacyMode;

        return paramsDict;
    }

    private OpenAIRequestData ParseOpenAIRequest(
        HttpRequestMessage request,
        string? requestBody,
        Dictionary<string, object>? requestJson
    )
    {
        var data = new OpenAIRequestData { RequestUri = request.RequestUri };

        if (requestJson != null)
        {
            try
            {
                // Extract common OpenAI parameters
                if (
                    requestJson.TryGetValue("model", out var modelValue)
                    && modelValue is JsonElement modelElement
                )
                {
                    data.Model = modelElement.GetString();
                }

                // Try to determine the endpoint type
                var path = request.RequestUri?.AbsolutePath ?? "";
                if (path.Contains("/chat/completions", StringComparison.Ordinal))
                {
                    data.EndpointType = OpenAIEndpointType.ChatCompletion;
                    if (requestJson.TryGetValue("messages", out var messagesValue))
                    {
                        data.Messages = messagesValue as JsonElement?;
                    }
                }
                else if (path.Contains("/embeddings", StringComparison.Ordinal))
                {
                    data.EndpointType = OpenAIEndpointType.Embedding;
                    if (requestJson.TryGetValue("input", out var inputValue))
                    {
                        data.Input = inputValue as JsonElement?;
                    }
                }
                else if (path.Contains("/audio/transcriptions", StringComparison.Ordinal))
                {
                    data.EndpointType = OpenAIEndpointType.Transcription;
                }

                // Extract other parameters for model parameters property
                data.ModelParameters = new Dictionary<string, object>();
                var paramKeys = new[]
                {
                    "temperature",
                    "max_tokens",
                    "max_completion_tokens",
                    "top_p",
                    "frequency_penalty",
                    "presence_penalty",
                    "n",
                    "stop",
                    "stream",
                    "response_format",
                    "seed",
                };

                foreach (var key in paramKeys)
                {
                    if (requestJson.TryGetValue(key, out var paramValue))
                    {
                        if (paramValue is JsonElement paramElement)
                        {
                            data.ModelParameters[key] = ConvertJsonElementToObject(paramElement);
                        }
                        else
                        {
                            data.ModelParameters[key] = paramValue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse OpenAI request JSON");
            }
        }

        return data;
    }

    private object ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt32(out var intVal)
                ? intVal
                : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element
                .EnumerateArray()
                .Select(ConvertJsonElementToObject)
                .ToList(),
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(prop => prop.Name, prop => ConvertJsonElementToObject(prop.Value)),
            JsonValueKind.Null => null!,
            _ => element.GetRawText(),
        };
    }

    private OpenAIResponseData ParseOpenAIResponse(string responseBody, HttpStatusCode statusCode)
    {
        var data = new OpenAIResponseData { StatusCode = statusCode };

        if (string.IsNullOrEmpty(responseBody))
            return data;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Extract model
            if (root.TryGetProperty("model", out var modelElement))
            {
                data.Model = modelElement.GetString();
            }

            // Extract usage information
            if (root.TryGetProperty("usage", out var usageElement))
            {
                data.Usage = new TokenUsage();

                if (usageElement.TryGetProperty("prompt_tokens", out var promptTokensElement))
                {
                    data.Usage.InputTokens = promptTokensElement.GetInt32();
                }

                if (
                    usageElement.TryGetProperty(
                        "completion_tokens",
                        out var completionTokensElement
                    )
                )
                {
                    data.Usage.OutputTokens = completionTokensElement.GetInt32();
                }

                if (usageElement.TryGetProperty("total_tokens", out var totalTokensElement))
                {
                    data.Usage.TotalTokens = totalTokensElement.GetInt32();
                }

                // Extract additional token details if available
                if (
                    usageElement.TryGetProperty(
                        "completion_tokens_details",
                        out var completionDetailsElement
                    )
                )
                {
                    if (
                        completionDetailsElement.TryGetProperty(
                            "reasoning_tokens",
                            out var reasoningTokensElement
                        )
                    )
                    {
                        data.Usage.ReasoningTokens = reasoningTokensElement.GetInt32();
                    }
                }

                if (
                    usageElement.TryGetProperty(
                        "prompt_tokens_details",
                        out var promptDetailsElement
                    )
                )
                {
                    if (
                        promptDetailsElement.TryGetProperty(
                            "cached_tokens",
                            out var cachedTokensElement
                        )
                    )
                    {
                        data.Usage.CacheReadInputTokens = cachedTokensElement.GetInt32();
                    }
                }
            }

            // Try to extract output for non-streaming responses
            if (
                root.TryGetProperty("choices", out var choicesElement)
                && choicesElement.ValueKind == JsonValueKind.Array
            )
            {
                data.HasOutput = true;
            }
            else if (root.TryGetProperty("output", out _))
            {
                data.HasOutput = true;
            }
            else if (root.TryGetProperty("data", out _))
            {
                data.HasOutput = true; // For embeddings
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse OpenAI response body");
        }

        return data;
    }

    private async Task<OpenAIResponseData> ProcessStreamingResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        var data = new OpenAIResponseData { StatusCode = response.StatusCode, IsStreaming = true };

        // For streaming responses, we don't accumulate the full content
        // but we can still capture metadata if available in headers or initial response
        // In a more complete implementation, we would parse SSE events

        // For now, read the response to get any metadata in the stream
        try
        {
#if NETSTANDARD2_1
            using var stream = await response.Content.ReadAsStreamAsync();
#else
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
#endif
            using var reader = new StreamReader(stream);

            string? line;
#if NETSTANDARD2_1
            while ((line = await reader.ReadLineAsync()) != null)
#else
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
#endif
            {
                if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    var jsonData = line.Substring(6);
                    if (jsonData == "[DONE]")
                        break;

                    try
                    {
                        using var doc = JsonDocument.Parse(jsonData);
                        var root = doc.RootElement;

                        // Extract model from first chunk if available
                        if (
                            data.Model == null
                            && root.TryGetProperty("model", out var modelElement)
                        )
                        {
                            data.Model = modelElement.GetString();
                        }

                        // Extract usage from last chunk
                        if (root.TryGetProperty("usage", out var usageElement))
                        {
                            data.Usage ??= new TokenUsage();

                            if (
                                usageElement.TryGetProperty(
                                    "prompt_tokens",
                                    out var promptTokensElement
                                )
                            )
                            {
                                data.Usage.InputTokens = promptTokensElement.GetInt32();
                            }

                            if (
                                usageElement.TryGetProperty(
                                    "completion_tokens",
                                    out var completionTokensElement
                                )
                            )
                            {
                                data.Usage.OutputTokens = completionTokensElement.GetInt32();
                            }

                            if (
                                usageElement.TryGetProperty(
                                    "total_tokens",
                                    out var totalTokensElement
                                )
                            )
                            {
                                data.Usage.TotalTokens = totalTokensElement.GetInt32();
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed JSON in streaming
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading streaming response");
        }

        return data;
    }

    private static string GetAIEventType(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";

        if (path.Contains("/embeddings", StringComparison.Ordinal))
        {
            return "$ai_embedding";
        }

        return "$ai_generation";
    }

    private async Task SendAIEventAsync(
        string eventType,
        OpenAIRequestData requestData,
        OpenAIResponseData? responseData,
        PosthogParams posthogParams,
        double latency,
        HttpStatusCode statusCode,
        CancellationToken cancellationToken,
        Exception? exception = null
    )
    {
        try
        {
            var properties = new Dictionary<string, object>
            {
                ["$ai_lib"] = "posthog-dotnet-ai",
                ["$ai_lib_version"] = "1.0.0",
                ["$ai_provider"] = posthogParams.ProviderOverride ?? "openai",
                ["$ai_model"] =
                    posthogParams.ModelOverride
                    ?? responseData?.Model
                    ?? requestData.Model
                    ?? "unknown",
                ["$ai_http_status"] = (int)statusCode,
                ["$ai_latency"] = latency,
                ["$ai_trace_id"] = posthogParams.TraceId,
                ["$ai_base_url"] = requestData.RequestUri?.GetLeftPart(UriPartial.Authority) ?? "",
            };

            // Add model parameters if available
            if (requestData.ModelParameters?.Count > 0)
            {
                properties["$ai_model_parameters"] = requestData.ModelParameters;
            }

            // Add token usage if available
            if (responseData?.Usage != null)
            {
                if (responseData.Usage.InputTokens.HasValue)
                {
                    properties["$ai_input_tokens"] = responseData.Usage.InputTokens.Value;
                }

                if (responseData.Usage.OutputTokens.HasValue)
                {
                    properties["$ai_output_tokens"] = responseData.Usage.OutputTokens.Value;
                }

                if (responseData.Usage.TotalTokens.HasValue)
                {
                    properties["$ai_total_tokens"] = responseData.Usage.TotalTokens.Value;
                }

                if (responseData.Usage.ReasoningTokens.HasValue)
                {
                    properties["$ai_reasoning_tokens"] = responseData.Usage.ReasoningTokens.Value;
                }

                if (responseData.Usage.CacheReadInputTokens.HasValue)
                {
                    properties["$ai_cache_read_input_tokens"] = responseData
                        .Usage
                        .CacheReadInputTokens
                        .Value;
                }
            }

            // Add streaming flag if applicable
            if (responseData?.IsStreaming == true)
            {
                properties["$ai_streaming"] = true;
            }

            // Add error information if applicable
            if (exception != null)
            {
                properties["$ai_is_error"] = true;
                properties["$ai_error"] = exception.Message;
            }
            // Add error flag for non-success HTTP status codes
            else if ((int)statusCode >= 400)
            {
                properties["$ai_is_error"] = true;
                properties["$ai_error"] = $"HTTP {(int)statusCode}";
            }

            // Merge with PostHog params from request
            if (posthogParams.Properties != null)
            {
                foreach (var kvp in posthogParams.Properties)
                {
                    properties[kvp.Key] = kvp.Value;
                }
            }

            // Merge with global options
            if (_options.Properties != null)
            {
                foreach (var kvp in _options.Properties)
                {
                    properties[kvp.Key] = kvp.Value;
                }
            }

            // Determine distinct ID (from request params, then options, then trace ID)
            var distinctId =
                posthogParams.DistinctId ?? _options.DefaultDistinctId ?? posthogParams.TraceId;

            // Determine groups (from request params, then options)
            var groupsDict = posthogParams.Groups ?? _options.Groups;
            GroupCollection? groups = null;
            if (groupsDict != null)
            {
                groups = new GroupCollection();
                foreach (var kvp in groupsDict)
                {
                    if (kvp.Value is string groupKey)
                    {
                        groups.Add(kvp.Key, groupKey);
                    }
                    else
                    {
                        groups.Add(kvp.Key, kvp.Value?.ToString() ?? "");
                    }
                }
            }

            // Determine if we should capture immediately
            var captureImmediate = posthogParams.CaptureImmediate || _options.CaptureImmediate;

            // Send the event
            _postHogClient.Capture(
                distinctId: distinctId,
                eventName: eventType,
                properties: properties,
                groups: groups,
                sendFeatureFlags: false
            );

            if (captureImmediate)
            {
                await _postHogClient.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send AI event to PostHog");
        }
    }

    private async Task ProcessStreamingTextAsync(
        HttpRequestMessage request,
        string? requestBody,
        Dictionary<string, object>? requestJson,
        string streamText,
        DateTimeOffset startTime,
        HttpStatusCode statusCode,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var latency = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
            var posthogParams = ExtractPosthogParams(requestJson);
            var requestData = ParseOpenAIRequest(request, requestBody, requestJson);
            var eventType = GetAIEventType(request);
            
            // Parse streaming text to extract metadata
            var responseData = new OpenAIResponseData { StatusCode = statusCode, IsStreaming = true };
            
            using var reader = new StringReader(streamText);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    var jsonData = line.Substring(6);
                    if (jsonData == "[DONE]")
                        break;

                    try
                    {
                        using var doc = JsonDocument.Parse(jsonData);
                        var root = doc.RootElement;

                        // Extract model from first chunk if available
                        if (
                            responseData.Model == null
                            && root.TryGetProperty("model", out var modelElement)
                        )
                        {
                            responseData.Model = modelElement.GetString();
                        }

                        // Extract usage from last chunk
                        if (root.TryGetProperty("usage", out var usageElement))
                        {
                            responseData.Usage ??= new TokenUsage();

                            if (
                                usageElement.TryGetProperty(
                                    "prompt_tokens",
                                    out var promptTokensElement
                                )
                            )
                            {
                                responseData.Usage.InputTokens = promptTokensElement.GetInt32();
                            }

                            if (
                                usageElement.TryGetProperty(
                                    "completion_tokens",
                                    out var completionTokensElement
                                )
                            )
                            {
                                responseData.Usage.OutputTokens = completionTokensElement.GetInt32();
                            }

                            if (
                                usageElement.TryGetProperty(
                                    "total_tokens",
                                    out var totalTokensElement
                                )
                            )
                            {
                                responseData.Usage.TotalTokens = totalTokensElement.GetInt32();
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed JSON in streaming
                    }
                }
            }

            await SendAIEventAsync(
                eventType,
                requestData,
                responseData,
                posthogParams,
                latency,
                statusCode,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing streaming text for PostHog");
        }
    }
}

public class OpenAIRequestData
{
    public string? Model { get; set; }
    public JsonElement? Messages { get; set; }
    public JsonElement? Input { get; set; }
    public Uri? RequestUri { get; set; }
    public OpenAIEndpointType EndpointType { get; set; }

    [SuppressMessage("Microsoft.Design", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public Dictionary<string, object>? ModelParameters { get; set; }
}

public class OpenAIResponseData
{
    public string? Model { get; set; }
    public TokenUsage? Usage { get; set; }
    public bool HasOutput { get; set; }
    public bool IsStreaming { get; set; }
    public HttpStatusCode StatusCode { get; set; }
}

public class TokenUsage
{
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? TotalTokens { get; set; }
    public int? ReasoningTokens { get; set; }
    public int? CacheReadInputTokens { get; set; }
}

public class PosthogParams
{
    public string? DistinctId { get; set; }
    public string TraceId { get; set; } = Guid.NewGuid().ToString();
    public bool PrivacyMode { get; set; }

    [SuppressMessage("Microsoft.Design", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public Dictionary<string, object>? Properties { get; set; }

    [SuppressMessage("Microsoft.Design", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public Dictionary<string, object>? Groups { get; set; }
    public string? ModelOverride { get; set; }
    public string? ProviderOverride { get; set; }
    public bool CaptureImmediate { get; set; }
}

public enum OpenAIEndpointType
{
    Unknown,
    ChatCompletion,
    Embedding,
    Transcription,
}
