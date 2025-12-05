using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PostHog.AI.OpenAI;

internal partial class OpenAIResponseParser
{
    private readonly ILogger _logger;

    public OpenAIResponseParser(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public OpenAIResponseData ParseOpenAIResponse(string responseBody, HttpStatusCode statusCode)
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
                data.OutputContent = OpenAIRequestParser.ConvertJsonElementToObject(choicesElement);
            }
            else if (root.TryGetProperty("output", out var outputElement))
            {
                data.HasOutput = true;
                data.OutputContent = OpenAIRequestParser.ConvertJsonElementToObject(outputElement);
            }
            else if (root.TryGetProperty("data", out var dataElement))
            {
                data.HasOutput = true; // For embeddings
                data.OutputContent = OpenAIRequestParser.ConvertJsonElementToObject(dataElement);
            }
        }
        catch (JsonException ex)
        {
            _logger.FailedToParseOpenAIResponseBody(ex);
        }

        return data;
    }

    public static async Task<OpenAIResponseData> ParseStreamingDataAsync(
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

    public async Task<OpenAIResponseData> ProcessStreamingResponseAsync(
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
            _logger.ErrorReadingOpenAIStreamingResponse(ex);
        }
#pragma warning restore CA1031

        return data;
    }
}

internal static partial class OpenAIResponseParserLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Failed to parse OpenAI response body")]
    public static partial void FailedToParseOpenAIResponseBody(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Error reading streaming response")]
    public static partial void ErrorReadingOpenAIStreamingResponse(this ILogger logger, Exception ex);
}