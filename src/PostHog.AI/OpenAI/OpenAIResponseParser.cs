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
            if (root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String)
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
                // Format output for PostHog
                data.SetOutputFormatted(OpenAIFormatter.FormatResponseOpenAI(root));
                // Calculate web search count
                data.Usage ??= new TokenUsage();
                data.Usage.WebSearchCount = OpenAIFormatter.CalculateWebSearchCount(root);
            }
            else if (root.TryGetProperty("output", out var outputElement))
            {
                data.HasOutput = true;
                data.OutputContent = OpenAIRequestParser.ConvertJsonElementToObject(outputElement);
                // Format output for PostHog
                data.SetOutputFormatted(OpenAIFormatter.FormatResponseOpenAI(root));
                // Calculate web search count
                data.Usage ??= new TokenUsage();
                data.Usage.WebSearchCount = OpenAIFormatter.CalculateWebSearchCount(root);
            }
            else if (root.TryGetProperty("data", out var dataElement))
            {
                data.HasOutput = true; // For embeddings
                data.OutputContent = OpenAIRequestParser.ConvertJsonElementToObject(dataElement);
                // Embeddings don't have formatted output or web search
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
                    if (data.Model == null && root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String)
                    {
                        data.Model = modelElement.GetString();
                    }

                    // Extract usage from last chunk
                    if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
                    {
                        data.Usage ??= new TokenUsage();

                        if (
                            usageElement.TryGetProperty(
                                "prompt_tokens",
                                out var promptTokensElement
                            ) && promptTokensElement.ValueKind == JsonValueKind.Number
                        )
                        {
                            data.Usage.InputTokens = promptTokensElement.GetInt32();
                        }

                        if (
                            usageElement.TryGetProperty(
                                "completion_tokens",
                                out var completionTokensElement
                            ) && completionTokensElement.ValueKind == JsonValueKind.Number
                        )
                        {
                            data.Usage.OutputTokens = completionTokensElement.GetInt32();
                        }

                        if (usageElement.TryGetProperty("total_tokens", out var totalTokensElement) && totalTokensElement.ValueKind == JsonValueKind.Number)
                        {
                            data.Usage.TotalTokens = totalTokensElement.GetInt32();
                        }
                    }

                    // Calculate web search count for this chunk
                    var chunkWebSearchCount = OpenAIFormatter.CalculateWebSearchCount(root);
                    if (chunkWebSearchCount > 0)
                    {
                        data.Usage ??= new TokenUsage();
                        if (chunkWebSearchCount > (data.Usage.WebSearchCount ?? 0))
                        {
                            data.Usage.WebSearchCount = chunkWebSearchCount;
                        }
                    }

                    // Accumulate streaming content
                    if (root.TryGetProperty("choices", out var choicesElement) && choicesElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var choice in choicesElement.EnumerateArray())
                        {
                            if (choice.ValueKind != JsonValueKind.Object) continue;
                            if (choice.TryGetProperty("delta", out var deltaElement) && deltaElement.ValueKind == JsonValueKind.Object)
                            {
                                // Accumulate text content
                                if (deltaElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                                {
                                    var content = contentElement.GetString();
                                    if (!string.IsNullOrEmpty(content))
                                    {
                                        data.AddAccumulatedContent(content);
                                    }
                                }
                                // Handle tool calls
                                if (deltaElement.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var toolCall in toolCallsElement.EnumerateArray())
                                    {
                                        if (toolCall.ValueKind != JsonValueKind.Object) continue;
                                        int? index = null;
                                        string? id = null;
                                        string? name = null;
                                        string? arguments = null;
                                        
                                        if (toolCall.TryGetProperty("index", out var indexElement) && indexElement.ValueKind == JsonValueKind.Number)
                                        {
                                            index = indexElement.GetInt32();
                                        }
                                        
                                        if (toolCall.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
                                        {
                                            id = idElement.GetString();
                                        }
                                        
                                        if (toolCall.TryGetProperty("function", out var functionElement) && functionElement.ValueKind == JsonValueKind.Object)
                                        {
                                            if (functionElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
                                            {
                                                name = nameElement.GetString();
                                            }
                                            
                                            if (functionElement.TryGetProperty("arguments", out var argsElement) && argsElement.ValueKind == JsonValueKind.String)
                                            {
                                                arguments = argsElement.GetString();
                                            }
                                        }
                                        
                                        if (index.HasValue)
                                        {
                                            data.AddOrUpdateToolCall(index.Value, id, name, arguments);
                                        }
                                    }
                                }
                            }
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
        // For streaming responses, parse the SSE events
        try
        {
#if NETSTANDARD2_1
            using var stream = await response.Content.ReadAsStreamAsync();
#else
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
#endif
            using var reader = new StreamReader(stream);

            var data = await ParseStreamingDataAsync(reader, cancellationToken);
            data.StatusCode = response.StatusCode;
            data.IsStreaming = true;
            
            // Build formatted output from accumulated streaming content
            data.BuildFormattedOutputFromStreaming();
            
            return data;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.ErrorReadingOpenAIStreamingResponse(ex);
            return new OpenAIResponseData { StatusCode = response.StatusCode, IsStreaming = true };
        }
#pragma warning restore CA1031
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