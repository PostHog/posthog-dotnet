using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog.AI.Utils;

namespace PostHog.AI.OpenAI;

public partial class PostHogOpenAIHandler : DelegatingHandler
{
    private readonly IPostHogClient _postHogClient;
    private readonly ILogger<PostHogOpenAIHandler> _logger;
    private readonly PostHogAIOptions _options;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly OpenAIRequestParser _requestParser;
    private readonly OpenAIResponseParser _responseParser;
    private readonly AIEventSender _aiEventSender;

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
        _requestParser = new OpenAIRequestParser(_logger, options, _jsonSerializerOptions);
        _responseParser = new OpenAIResponseParser(_logger);
        _aiEventSender = new AIEventSender(_postHogClient, _logger, options);
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
                    _logger.ErrorReadingStreamingResponse(null);
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
                        _logger.FailedToParseRequestBody(ex);
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
                        _logger.FailedToParseResponseBody(null);
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
                        _logger.ErrorProcessingError(innerEx);
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
            _logger.FailedToParseMultipartRequest(ex);
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
            var posthogParams = _requestParser.ExtractPosthogParams(request, requestJson);

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
                responseData = await _responseParser
                    .ProcessStreamingResponseAsync(response, cancellationToken)
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
                responseData = _responseParser.ParseOpenAIResponse(
                    responseBodyToParse ?? string.Empty,
                    response.StatusCode
                );
            }

            // Parse request data
            var requestData = _requestParser.ParseOpenAIRequest(request, requestBody, requestJson);

            // Determine event type based on endpoint
            var eventType = GetAIEventType(request);

            // Send event to PostHog
            try
            {
                await _aiEventSender
                    .SendAIEventAsync(
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
#pragma warning disable CA1031, CS0168
            catch (Exception ex)
            {
                _ = ex;
                // Ignore - AIEventSender already logged the error
            }
#pragma warning restore CA1031, CS0168
        }
#pragma warning disable CA1031, CS0168
        catch (Exception ex)
        {
            // Ignore - AIEventSender already logged the error
        }
#pragma warning restore CA1031, CS0168
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
#pragma warning disable CA1031 // Do not catch general exception types
        try
        {
            var latency = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
            var posthogParams = _requestParser.ExtractPosthogParams(request, requestJson);
            var requestData = _requestParser.ParseOpenAIRequest(request, requestBody, requestJson);
            var eventType = GetAIEventType(request);

            // Send error event to PostHog
            try
            {
                await _aiEventSender
                    .SendAIEventAsync(
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
            catch
            {
                // Ignore - AIEventSender already logged the error
            }
        }
        catch
        {
            // Ignore - AIEventSender already logged the error
        }
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
            var posthogParams = _requestParser.ExtractPosthogParams(request, requestJson);
            var requestData = _requestParser.ParseOpenAIRequest(request, requestBody, requestJson);
            var eventType = GetAIEventType(request);

            // Parse streaming text to extract metadata
            var responseData = new OpenAIResponseData
            {
                StatusCode = statusCode,
                IsStreaming = true,
            };

# pragma warning disable CA1848
# pragma warning disable CA2254
            this._logger.LogInformation(streamText);

            using var reader = new StringReader(streamText);
            var parsedData = await OpenAIResponseParser.ParseStreamingDataAsync(
                reader,
                CancellationToken.None
            );
            responseData.Model = parsedData.Model;
            responseData.Usage = parsedData.Usage;
            // Build formatted output from accumulated streaming content
            parsedData.BuildFormattedOutputFromStreaming();
            if (parsedData.OutputFormatted != null)
            {
                responseData.SetOutputFormatted(parsedData.OutputFormatted);
                responseData.HasOutput = true;
            }

            await _aiEventSender.SendAIEventAsync(
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
            _logger.ErrorProcessingStreamingText(ex);
        }
    }
}

internal static partial class PostHogOpenAIHandlerLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Failed to parse OpenAI request body as JSON"
    )]
    public static partial void FailedToParseRequestBody(this ILogger logger, Exception? ex);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Failed to parse multipart request"
    )]
    public static partial void FailedToParseMultipartRequest(this ILogger logger, Exception? ex);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Debug,
        Message = "Failed to parse OpenAI response body"
    )]
    public static partial void FailedToParseResponseBody(this ILogger logger, Exception? ex);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Error,
        Message = "Error processing OpenAI error for PostHog"
    )]
    public static partial void ErrorProcessingError(this ILogger logger, Exception? ex);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Debug,
        Message = "Error reading streaming response"
    )]
    public static partial void ErrorReadingStreamingResponse(this ILogger logger, Exception? ex);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Error,
        Message = "Error processing streaming text for PostHog"
    )]
    public static partial void ErrorProcessingStreamingText(this ILogger logger, Exception? ex);
}
