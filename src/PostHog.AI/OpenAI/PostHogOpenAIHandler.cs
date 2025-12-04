using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
                // Restore the request content since we consumed it
                var mediaType =
                    request.Content.Headers.ContentType?.MediaType ?? "application/json";
                request.Content = new StringContent(requestBody, Encoding.UTF8, mediaType);

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
                    using var stream = await response
                        .Content.ReadAsStreamAsync(cancellationToken)
                        .ConfigureAwait(false);
#endif
                    // We need to return a new stream that wraps the original stream
                    // and captures the content as it passes through.
                    var observabilityStream = new PostHogObservabilityStream(
                        stream,
                        async (text) =>
                            await ProcessStreamingTextAsync(
                                    request,
                                    requestBody,
                                    requestJson,
                                    text,
                                    startTime,
                                    response.StatusCode,
                                    cancellationToken // Pass cancellation token but the callback runs on fire-and-forget
                                )
                                .ConfigureAwait(false)
                    );

                    response.Content = new StreamContent(observabilityStream);
                    foreach (var header in response.Content.Headers)
                    {
                        // Copy headers from original content?
                        // Actually we need to be careful. The original response headers are on `response.Content.Headers`.
                        // We replaced `response.Content`.
                    }
                    // Ideally we should have copied the headers.
                    // But `StreamContent` constructor doesn't take headers.
                    // Let's set the critical one.
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue(
                        "text/event-stream"
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
                    responseBody = await response
                        .Content.ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
#endif
                    // Restore the response content since we consumed it
                    response.Content = new StringContent(
                        responseBody,
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
                () =>
                    ProcessErrorAsync(
                        request,
                        requestBody,
                        requestJson,
                        ex,
                        startTime,
                        CancellationToken.None
                    ),
                CancellationToken.None
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
            catch { }
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
            catch { }
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
            ["posthogDistinctId"] = v =>
            {
                if (v != null)
                    paramsDict.DistinctId = v.ToString();
            },
            ["posthogTraceId"] = v =>
            {
                if (v != null)
                    paramsDict.TraceId = v.ToString() ?? paramsDict.TraceId;
            },
            ["posthogPrivacyMode"] = v =>
            {
                if (v is JsonElement je && je.ValueKind == JsonValueKind.True)
                    paramsDict.PrivacyMode = true;
                else if (v is bool b && b)
                    paramsDict.PrivacyMode = true;
            },
            ["posthogModelOverride"] = v =>
            {
                if (v != null)
                    paramsDict.ModelOverride = v.ToString();
            },
            ["posthogProviderOverride"] = v =>
            {
                if (v != null)
                    paramsDict.ProviderOverride = v.ToString();
            },
            ["posthogCostOverride"] = v =>
            {
                if (v != null)
                    paramsDict.CostOverride = v.ToString();
            },
            ["posthogWebSearchCount"] = v =>
            {
                if (
                    v is JsonElement je
                    && je.ValueKind == JsonValueKind.Number
                    && je.TryGetInt32(out int count)
                )
                    paramsDict.WebSearchCount = count;
                else if (
                    v is JsonElement je2
                    && je2.ValueKind == JsonValueKind.Number
                    && je2.TryGetDouble(out double dCount)
                )
                    paramsDict.WebSearchCount = (int)dCount;
                else if (v != null)
                {
                    try
                    {
                        paramsDict.WebSearchCount = Convert.ToInt32(
                            v,
                            CultureInfo.InvariantCulture
                        );
                    }
                    catch { }
                }
            },
            ["posthogCaptureImmediate"] = v =>
            {
                if (v is JsonElement je && je.ValueKind == JsonValueKind.True)
                    paramsDict.CaptureImmediate = true;
                else if (v is bool b && b)
                    paramsDict.CaptureImmediate = true;
            },
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
#pragma warning disable CA1848
                    _failedToSetParameter(_logger, kvp.Key, ex);
#pragma warning restore CA1848
                }
            }
        }

        // Extract properties and groups if they exist as JSON objects
        if (requestJson.TryGetValue("posthogProperties", out var propertiesValue))
        {
            try
            {
                var bodyProps = propertiesValue is JsonElement propertiesElement
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

        if (requestJson.TryGetValue("posthogGroups", out var groupsValue))
        {
            try
            {
                var bodyGroups = groupsValue is JsonElement groupsElement
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
            catch (Exception ex)
            {
#pragma warning disable CA1848
                _failedToParseRequestJson(_logger, ex);
#pragma warning restore CA1848
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
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
#else
            while (
                (line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null
            )
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
#pragma warning disable CA1848
            _errorReadingStreamingResponse(_logger, ex);
#pragma warning restore CA1848
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
            properties["$ai_input"] = Sanitizer.Sanitize(inputContent)!;

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
            properties["$ai_output_choices"] = Sanitizer.Sanitize(outputContent)!;

            // Add model parameters if available
            if (requestData.ModelParameters?.Count > 0)
            {
                properties["$ai_model_parameters"] = requestData.ModelParameters;
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
                                responseData.Usage.OutputTokens =
                                    completionTokensElement.GetInt32();
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
#pragma warning disable CA1848
            _errorProcessingStreamingText(_logger, ex);
#pragma warning restore CA1848
        }
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
