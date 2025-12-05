using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog.AI.Utils;

#pragma warning disable CA1848, CA1031, CA1307, CA1310, CA1822, CA2016

namespace PostHog.AI.OpenAI;

public class PostHogOpenAIHandler : DelegatingHandler
{
    private readonly IPostHogClient _postHogClient;
    private readonly ILogger<PostHogOpenAIHandler> _logger;
    private readonly PostHogAIOptions _options;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    private static readonly Action<ILogger, Exception?> _failedToParseRequestBody =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(1, "FailedToParseRequestBody"),
            "Failed to parse OpenAI request body as JSON"
        );

    private static readonly Action<ILogger, Exception?> _failedToParseMultipartRequest =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(2, "FailedToParseMultipartRequest"),
            "Failed to parse multipart request"
        );

    private static readonly Action<ILogger, Exception?> _failedToParseResponseBody =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(3, "FailedToParseResponseBody"),
            "Failed to parse OpenAI response body"
        );

    private static readonly Action<ILogger, Exception?> _failedToParseRequestJson =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(4, "FailedToParseRequestJson"),
            "Failed to parse OpenAI request JSON"
        );

    private static readonly Action<ILogger, Exception?> _errorProcessingRequest =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(5, "ErrorProcessingRequest"),
            "Error processing OpenAI request for PostHog"
        );

    private static readonly Action<ILogger, Exception?> _errorProcessingError =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(6, "ErrorProcessingError"),
            "Error processing OpenAI error for PostHog"
        );

    private static readonly Action<ILogger, Exception?> _errorReadingStreamingResponse =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(7, "ErrorReadingStreamingResponse"),
            "Error reading streaming response"
        );

    private static readonly Action<ILogger, string, Exception?> _failedToSetParameter =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(8, "FailedToSetParameter"),
            "Failed to set PostHog parameter {Parameter}"
        );

    private static readonly Action<ILogger, Exception?> _failedToSendEvent = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(9, "FailedToSendEvent"),
        "Failed to send AI event to PostHog"
    );

    private static readonly Action<ILogger, Exception?> _errorProcessingStreamingText =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(10, "ErrorProcessingStreamingText"),
            "Error processing streaming text for PostHog"
        );

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
            MaxDepth = PostHogAIConstants.MaxJsonDepth,
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
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var startTime = DateTimeOffset.UtcNow;
        string? requestBody = null;
        Dictionary<string, object>? requestJson = null;

        // Read and buffer the request content
        if (request.Content != null)
        {
            var isMultipart =
                request.Content.Headers.ContentType?.MediaType?.Contains(
                    "multipart",
                    StringComparison.OrdinalIgnoreCase
                ) ?? false;

            if (isMultipart)
            {
#if NETSTANDARD2_1
                var bytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#else
                var bytes = await request
                    .Content.ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
#endif
                if (bytes.Length > 0 && request.Content.Headers.ContentType != null)
                {
                    requestJson = ParseMultipartRequest(bytes, request.Content.Headers.ContentType);

                    // Restore content
                    var newContent = new ByteArrayContent(bytes);
                    foreach (var h in request.Content.Headers)
                        newContent.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    request.Content = newContent;
                }
            }
            else
            {
#if NETSTANDARD2_1
                requestBody = await request.Content.ReadAsStringAsync();
#else
                requestBody = await request
                    .Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
#endif
                // Check request body size limit
                if (requestBody.Length > PostHogAIConstants.MaxRequestBodySize)
                {
                    _logger.LogWarning(
                        "Request body size {Size} exceeds limit {Limit}, skipping JSON parsing",
                        requestBody.Length,
                        PostHogAIConstants.MaxRequestBodySize
                    );
                    // Still restore content but skip JSON parsing below
                    requestJson = null;
                }

                // Restore the request content since we consumed it
                var mediaType =
                    request.Content.Headers.ContentType?.MediaType ?? "application/json";
                request.Content = new StringContent(requestBody, Encoding.UTF8, mediaType);

                // Parse request body for PostHog parameters
                if (
                    !string.IsNullOrEmpty(requestBody)
                    && requestBody.Length <= PostHogAIConstants.MaxRequestBodySize
                )
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
                        _failedToParseRequestBody(_logger, ex);
                    }
                }
            }
        }

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // Determine if this is a streaming response
            var isStreaming =
                response.Content?.Headers.ContentType?.MediaType == "text/event-stream"
                || (
                    requestJson?.ContainsKey("stream") == true
                    && requestJson["stream"] is JsonElement streamElement
                    && streamElement.ValueKind == JsonValueKind.True
                );

            if (isStreaming)
            {
                // For streaming responses, we wrap the stream to capture the content as it passes through
                // This avoids buffering the entire response in memory before returning to the caller
                if (response.Content != null)
                {
#if NETSTANDARD2_1
                    var stream = await response.Content.ReadAsStreamAsync();
#else
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
#endif
                    var observabilityStream = new PostHogObservabilityStream(
                        stream,
                        async (streamText) =>
                        {
                            // We use CancellationToken.None here to ensure we capture the event
                            // even if the original request was cancelled
                            await ProcessStreamingTextAsync(
                                request,
                                requestBody,
                                requestJson,
                                streamText,
                                startTime,
                                response.StatusCode,
                                CancellationToken.None
                            );
                        },
                        PostHogAIConstants.MaxStreamBufferSize
                    );

                    // Create new stream content preserving headers
                    var newContent = new StreamContent(observabilityStream);
                    foreach (var header in response.Content.Headers)
                    {
                        newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                    response.Content = newContent;
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
                    responseBody = await response
                        .Content.ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
#endif
                    // Check response body size limit
                    if (responseBody.Length > PostHogAIConstants.MaxResponseBodySize)
                    {
                        _logger.LogWarning(
                            "Response body size {Size} exceeds limit {Limit}, skipping detailed parsing",
                            responseBody.Length,
                            PostHogAIConstants.MaxResponseBodySize
                        );
                        responseBody = null;
                    }

                    // Restore the response content since we consumed it
                    response.Content = new StringContent(
                        responseBody ?? string.Empty,
                        Encoding.UTF8,
                        response.Content.Headers.ContentType?.MediaType ?? "application/json"
                    );
                }

                await ProcessRequestAsync(
                        request,
                        requestBody,
                        requestJson,
                        response,
                        startTime,
                        responseBody,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                return response;
            }
        }
#pragma warning disable CA1031 // Do not catch general exception types - we must catch everything to ensure we don't break the user's app
        catch (Exception ex)
        {
            // If request fails, still try to capture the error
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await ProcessErrorAsync(
                            request,
                            requestBody,
                            requestJson,
                            ex,
                            startTime,
                            cancellationToken
                        );
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(
                            innerEx,
                            "Error in background error processing of OpenAI request for PostHog"
                        );
                    }
                },
                cancellationToken
            );
            throw;
        }
#pragma warning restore CA1031
    }

    private static bool IsOpenAIRequest(HttpRequestMessage request)
    {
        var uri = request.RequestUri?.ToString() ?? "";
        return uri.Contains("openai.azure.com", StringComparison.Ordinal)
            || uri.Contains("api.openai.com", StringComparison.Ordinal)
            || uri.Contains(".openai.azure.com", StringComparison.Ordinal);
    }

    private Dictionary<string, object>? ParseMultipartRequest(
        byte[] content,
        MediaTypeHeaderValue contentType
    )
    {
        var dict = new Dictionary<string, object>();
        var boundary = contentType
            .Parameters.FirstOrDefault(p => p.Name == "boundary")
            ?.Value?.Trim('"');
        if (string.IsNullOrEmpty(boundary))
            return null;

        try
        {
            // Naive parsing: convert to string.
            // Binary parts will be garbled but headers and text fields (model) should remain intact.
            var contentString = Encoding.UTF8.GetString(content);
            var parts = contentString.Split(
                new[] { "--" + boundary },
                StringSplitOptions.RemoveEmptyEntries
            );

            foreach (var part in parts)
            {
                if (
                    part.StartsWith("--", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(part)
                )
                    continue;

                // Find double newline which separates headers from content
                // Normalize newlines slightly?
                var endOfHeaders = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (endOfHeaders < 0)
                    endOfHeaders = part.IndexOf("\n\n", StringComparison.Ordinal);

                if (endOfHeaders > 0)
                {
                    var headers = part.Substring(0, endOfHeaders);
                    var body = part.Substring(endOfHeaders).Trim();

                    if (headers.Contains("name=\"model\"", StringComparison.OrdinalIgnoreCase))
                    {
                        dict["model"] = body.Trim();
                    }
                    else if (headers.Contains("filename=\"", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = Regex.Match(headers, "filename=\"([^\"]+)\"");
                        dict["input"] = match.Success ? match.Groups[1].Value : "[File]";
                    }
                }
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
#pragma warning disable CA1848
            _failedToParseMultipartRequest(_logger, ex);
#pragma warning restore CA1848
        }
#pragma warning restore CA1031
        return dict.Count > 0 ? dict : null;
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
        await ProcessRequestAsync(
                request,
                requestBody,
                requestJson,
                response,
                startTime,
                responseBody: null,
                cancellationToken
            )
            .ConfigureAwait(false);
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
            var posthogParams = ExtractPosthogParams(request, requestJson);

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
                responseData = await ProcessStreamingResponseAsync(response, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                string? responseBodyToParse = responseBody;
                if (responseBodyToParse == null && response.Content != null)
                {
#if NETSTANDARD2_1
                    responseBodyToParse = await response.Content.ReadAsStringAsync();
#else
                    responseBodyToParse = await response
                        .Content.ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
#endif
                }
                responseData = ParseOpenAIResponse(
                    responseBodyToParse ?? string.Empty,
                    response.StatusCode
                );
            }

            // Parse request data
            var requestData = ParseOpenAIRequest(request, requestBody, requestJson);

            // Determine event type based on endpoint
            var eventType = GetAIEventType(request);

            // Send event to PostHog
            try
            {
                await SendAIEventAsync(
                        eventType,
                        requestData,
                        responseData,
                        posthogParams,
                        latency,
                        response.StatusCode,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                _failedToSendEvent(_logger, ex);
            }
#pragma warning restore CA1031
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _errorProcessingRequest(_logger, ex);
        }
#pragma warning restore CA1031
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
            var posthogParams = ExtractPosthogParams(request, requestJson);
            var requestData = ParseOpenAIRequest(request, requestBody, requestJson);
            var eventType = GetAIEventType(request);

            // Send error event to PostHog
            try
            {
                await SendAIEventAsync(
                        eventType,
                        requestData,
                        null,
                        posthogParams,
                        latency,
                        HttpStatusCode.InternalServerError,
                        cancellationToken,
                        exception
                    )
                    .ConfigureAwait(false);
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                _failedToSendEvent(_logger, ex);
            }
#pragma warning restore CA1031
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _errorProcessingError(_logger, ex);
        }
#pragma warning restore CA1031
    }

    private PosthogParams ExtractPosthogParams(
        HttpRequestMessage request,
        Dictionary<string, object>? requestJson
    )
    {
        var paramsDict = new PosthogParams();

        // 1. Extract from Headers (Higher priority for context, but lower for "overrides" usually?
        // Actually, explicit headers usually override config, and body overrides headers/config.
        // But body params are often hard to set. Let's assume Headers > Body for "context" like DistinctId,
        // but maybe we should allow both.
        // Let's go with: Body > Headers > Options (defaults).

        // Helper to get header value
        string? GetHeader(string key)
        {
            if (request.Headers.TryGetValues(key, out var values))
            {
                return values.FirstOrDefault();
            }
            return null;
        }

        paramsDict.DistinctId = GetHeader("x-posthog-distinct-id");
        paramsDict.TraceId = GetHeader("x-posthog-trace-id") ?? paramsDict.TraceId;

        if (
            GetHeader("x-posthog-privacy-mode") is string privacyVal
            && bool.TryParse(privacyVal, out var privacy)
        )
            paramsDict.PrivacyMode = privacy;

        paramsDict.ModelOverride = GetHeader("x-posthog-model-override");
        paramsDict.ProviderOverride = GetHeader("x-posthog-provider-override");
        paramsDict.CostOverride = GetHeader("x-posthog-cost-override");

        if (int.TryParse(GetHeader("x-posthog-web-search-count"), out var searchCount))
            paramsDict.WebSearchCount = searchCount;

        if (bool.TryParse(GetHeader("x-posthog-capture-immediate"), out var captureImm))
            paramsDict.CaptureImmediate = captureImm;

        if (GetHeader("x-posthog-properties") is string propsJson)
        {
            try
            {
                paramsDict.Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    propsJson,
                    _jsonSerializerOptions
                );
            }
#pragma warning disable CA1031
            catch { }
#pragma warning restore CA1031
        }

        if (GetHeader("x-posthog-groups") is string groupsJson)
        {
            try
            {
                paramsDict.Groups = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    groupsJson,
                    _jsonSerializerOptions
                );
            }
#pragma warning disable CA1031
            catch { }
#pragma warning restore CA1031
        }

        if (requestJson == null)
        {
            // Apply defaults if no body
            ApplyDefaults(paramsDict);
            return paramsDict;
        }

        // 2. Extract from Body (overrides headers)
        // Map of PostHog parameter names from request JSON to our params object
        var paramMappings = new Dictionary<string, Action<object>>
        {
            [PostHogAIConstants.ParamDistinctId] = v => paramsDict.DistinctId = v?.ToString(),
            [PostHogAIConstants.ParamTraceId] = v =>
                paramsDict.TraceId = v?.ToString() ?? Guid.NewGuid().ToString(),
            [PostHogAIConstants.ParamPrivacyMode] = v =>
                paramsDict.PrivacyMode = v is JsonElement je && je.ValueKind == JsonValueKind.True,
            [PostHogAIConstants.ParamModelOverride] = v => paramsDict.ModelOverride = v?.ToString(),
            [PostHogAIConstants.ParamProviderOverride] = v =>
                paramsDict.ProviderOverride = v?.ToString(),
            [PostHogAIConstants.ParamCaptureImmediate] = v =>
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
#pragma warning disable CA1031
                catch (Exception ex)
                {
#pragma warning disable CA1848
                    _failedToSetParameter(_logger, kvp.Key, ex);
#pragma warning restore CA1848
                }
#pragma warning restore CA1031
            }
        }

        // Extract properties and groups if they exist as JSON objects
        if (
            requestJson.TryGetValue(PostHogAIConstants.ParamProperties, out var propertiesValue)
            && propertiesValue is JsonElement propertiesElement
        )
        {
            try
            {
                var bodyProps =
                    propertiesValue is JsonElement
                        ? JsonSerializer.Deserialize<Dictionary<string, object>>(
                            propertiesElement.GetRawText(),
                            _jsonSerializerOptions
                        )
                        : propertiesValue as Dictionary<string, object>;

                if (bodyProps != null)
                {
                    paramsDict.Properties ??= new Dictionary<string, object>();
                    foreach (var kvp in bodyProps)
                        paramsDict.Properties[kvp.Key] = kvp.Value;
                }
            }
            catch (JsonException) { }
        }

        if (
            requestJson.TryGetValue(PostHogAIConstants.ParamGroups, out var groupsValue)
            && groupsValue is JsonElement groupsElement
        )
        {
            try
            {
                var bodyGroups =
                    groupsValue is JsonElement
                        ? JsonSerializer.Deserialize<Dictionary<string, object>>(
                            groupsElement.GetRawText(),
                            _jsonSerializerOptions
                        )
                        : groupsValue as Dictionary<string, object>;

                if (bodyGroups != null)
                {
                    paramsDict.Groups ??= new Dictionary<string, object>();
                    foreach (var kvp in bodyGroups)
                        paramsDict.Groups[kvp.Key] = kvp.Value;
                }
            }
            catch (JsonException) { }
        }

        ApplyDefaults(paramsDict);
        return paramsDict;
    }

    private void ApplyDefaults(PosthogParams paramsDict)
    {
        // Set defaults
        if (string.IsNullOrEmpty(paramsDict.TraceId))
        {
            paramsDict.TraceId = Guid.NewGuid().ToString();
        }

        paramsDict.PrivacyMode = paramsDict.PrivacyMode || _options.PrivacyMode;
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
                        data.Messages = messagesValue is JsonElement messagesElement
                            ? ConvertJsonElementToObject(messagesElement)
                            : messagesValue;
                    }
                    if (requestJson.TryGetValue("tools", out var toolsValue))
                    {
                        data.Tools = toolsValue is JsonElement toolsElement
                            ? ConvertJsonElementToObject(toolsElement)
                            : toolsValue;
                    }
                }
                else if (path.Contains("/embeddings", StringComparison.Ordinal))
                {
                    data.EndpointType = OpenAIEndpointType.Embedding;
                    if (requestJson.TryGetValue("input", out var inputValue))
                    {
                        data.Input = inputValue is JsonElement inputElement
                            ? ConvertJsonElementToObject(inputElement)
                            : inputValue;
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
                    "tool_choice",
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
#pragma warning disable CA1031
            catch (Exception ex)
            {
#pragma warning disable CA1848
                _failedToParseRequestJson(_logger, ex);
#pragma warning restore CA1848
            }
#pragma warning restore CA1031
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
                data.OutputContent = ConvertJsonElementToObject(choicesElement);
            }
            else if (root.TryGetProperty("output", out var outputElement))
            {
                data.HasOutput = true;
                data.OutputContent = ConvertJsonElementToObject(outputElement);
            }
            else if (root.TryGetProperty("data", out var dataElement))
            {
                data.HasOutput = true; // For embeddings
                data.OutputContent = ConvertJsonElementToObject(dataElement);
            }
        }
        catch (JsonException ex)
        {
#pragma warning disable CA1848
            _failedToParseResponseBody(_logger, ex);
#pragma warning restore CA1848
        }

        return data;
    }

    private async Task<OpenAIResponseData> ParseStreamingDataAsync(
        TextReader reader,
        CancellationToken cancellationToken
    )
    {
        var data = new OpenAIResponseData { IsStreaming = true };

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
                    if (data.Model == null && root.TryGetProperty("model", out var modelElement))
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

                        if (usageElement.TryGetProperty("total_tokens", out var totalTokensElement))
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

            var parsedData = await ParseStreamingDataAsync(reader, cancellationToken);
            data.Model = parsedData.Model;
            data.Usage = parsedData.Usage;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
#pragma warning disable CA1848
            _errorReadingStreamingResponse(_logger, ex);
#pragma warning restore CA1848
        }
#pragma warning restore CA1031

        return data;
    }

    private static string GetAIEventType(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";

        if (path.Contains("/embeddings", StringComparison.Ordinal))
        {
            return PostHogAIConstants.EventTypeEmbedding;
        }

        return PostHogAIConstants.EventTypeGeneration;
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
                [PostHogAIConstants.PropertyLib] = PostHogAIConstants.DefaultLibName,
                [PostHogAIConstants.PropertyLibVersion] = PostHogAIConstants.DefaultLibVersion,
                [PostHogAIConstants.PropertyProvider] =
                    posthogParams.ProviderOverride ?? PostHogAIConstants.DefaultProvider,
                [PostHogAIConstants.PropertyModel] =
                    posthogParams.ModelOverride
                    ?? responseData?.Model
                    ?? requestData.Model
                    ?? "unknown",
                [PostHogAIConstants.PropertyHttpStatus] = (int)statusCode,
                [PostHogAIConstants.PropertyLatency] = latency,
                [PostHogAIConstants.PropertyTraceId] = posthogParams.TraceId,
                [PostHogAIConstants.PropertyBaseUrl] =
                    requestData.RequestUri?.GetLeftPart(UriPartial.Authority) ?? "",
            };

            // Add input content with privacy mode
            object? inputContent = null;
            if (!posthogParams.PrivacyMode)
            {
                if (requestData.EndpointType == OpenAIEndpointType.ChatCompletion)
                {
                    inputContent = requestData.Messages;
                }
                else if (requestData.EndpointType == OpenAIEndpointType.Embedding)
                {
                    inputContent = requestData.Input;
                }
                // For other endpoint types, inputContent remains null
            }
            properties[PostHogAIConstants.PropertyInput] = Sanitizer.Sanitize(inputContent)!;

            // Add output content with privacy mode
            object? outputContent = null;
            if (!posthogParams.PrivacyMode && responseData != null && !responseData.IsStreaming)
            {
                outputContent = responseData.OutputContent;
                // For embeddings, set output to null (following JavaScript package)
                if (requestData.EndpointType == OpenAIEndpointType.Embedding)
                {
                    outputContent = null;
                }
            }
            properties[PostHogAIConstants.PropertyOutputChoices] = Sanitizer.Sanitize(
                outputContent
            )!;

            // Add model parameters if available
            if (requestData.ModelParameters?.Count > 0)
            {
                properties[PostHogAIConstants.PropertyModelParameters] =
                    requestData.ModelParameters;
            }

            // Add tools if available
            if (requestData.Tools != null)
            {
                properties["$ai_tools"] = requestData.Tools;
            }

            // Add web search count if available
            if (posthogParams.WebSearchCount.HasValue)
            {
                properties["$ai_web_search_count"] = posthogParams.WebSearchCount.Value;
            }

            // Add cost override if available
            if (posthogParams.CostOverride != null)
            {
                properties["$ai_cost_override"] = posthogParams.CostOverride;
            }

            // Add token usage if available
            if (responseData?.Usage != null)
            {
                if (responseData.Usage.InputTokens.HasValue)
                {
                    properties[PostHogAIConstants.PropertyInputTokens] = responseData
                        .Usage
                        .InputTokens
                        .Value;
                }

                if (responseData.Usage.OutputTokens.HasValue)
                {
                    properties[PostHogAIConstants.PropertyOutputTokens] = responseData
                        .Usage
                        .OutputTokens
                        .Value;
                }

                if (responseData.Usage.TotalTokens.HasValue)
                {
                    properties[PostHogAIConstants.PropertyTotalTokens] = responseData
                        .Usage
                        .TotalTokens
                        .Value;
                }

                if (responseData.Usage.ReasoningTokens.HasValue)
                {
                    properties[PostHogAIConstants.PropertyReasoningTokens] = responseData
                        .Usage
                        .ReasoningTokens
                        .Value;
                }

                if (responseData.Usage.CacheReadInputTokens.HasValue)
                {
                    properties[PostHogAIConstants.PropertyCacheReadInputTokens] = responseData
                        .Usage
                        .CacheReadInputTokens
                        .Value;
                }
            }

            // Add streaming flag if applicable
            if (responseData?.IsStreaming == true)
            {
                properties[PostHogAIConstants.PropertyStreaming] = true;
            }

            // Add error information if applicable
            if (exception != null)
            {
                properties[PostHogAIConstants.PropertyIsError] = true;
                properties[PostHogAIConstants.PropertyError] = exception.Message;
            }
            // Add error flag for non-success HTTP status codes
            else if ((int)statusCode >= 400)
            {
                properties[PostHogAIConstants.PropertyIsError] = true;
                properties[PostHogAIConstants.PropertyError] = $"HTTP {(int)statusCode}";
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

            // Add $process_person_profile: false when no distinctId was provided (following JavaScript package)
            if (posthogParams.DistinctId == null && _options.DefaultDistinctId == null)
            {
                properties["$process_person_profile"] = false;
            }

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
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _failedToSendEvent(_logger, ex);
        }
#pragma warning restore CA1031
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
            var posthogParams = ExtractPosthogParams(request, requestJson);
            var requestData = ParseOpenAIRequest(request, requestBody, requestJson);
            var eventType = GetAIEventType(request);

            // Parse streaming text to extract metadata
            var responseData = new OpenAIResponseData
            {
                StatusCode = statusCode,
                IsStreaming = true,
            };

            using var reader = new StringReader(streamText);
            var parsedData = await ParseStreamingDataAsync(reader, CancellationToken.None);
            responseData.Model = parsedData.Model;
            responseData.Usage = parsedData.Usage;

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
#pragma warning disable CA1031
        catch (Exception ex)
        {
#pragma warning disable CA1848
            _errorProcessingStreamingText(_logger, ex);
#pragma warning restore CA1848
        }
#pragma warning restore CA1031
    }
}

public class OpenAIRequestData
{
    public string? Model { get; set; }
    public object? Messages { get; set; }
    public object? Input { get; set; }
    public object? Tools { get; set; }
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
    public object? OutputContent { get; set; }
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
    public string? CostOverride { get; set; }
    public int? WebSearchCount { get; set; }
    public bool CaptureImmediate { get; set; }
}

public enum OpenAIEndpointType
{
    Unknown,
    ChatCompletion,
    Embedding,
    Transcription,
}
