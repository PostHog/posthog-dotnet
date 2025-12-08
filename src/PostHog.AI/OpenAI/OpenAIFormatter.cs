using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PostHog.AI.OpenAI;

public class FormattedMessage
{
    public string Role { get; set; } = "assistant";
    public object Content { get; set; } = new List<FormattedContentItem>();
}

public class FormattedContentItem
{
    public string Type { get; set; } = "text";
    public string? Text { get; set; }
    public string? Id { get; set; }
    public FormattedFunctionCall? Function { get; set; }
    public string? Image { get; set; }
}

public class FormattedFunctionCall
{
    public string Name { get; set; } = "";
    public object Arguments { get; set; } = "";
}

public static class OpenAIFormatter
{
    public static IReadOnlyList<FormattedMessage> FormatResponseOpenAI(object response)
    {
        var output = new List<FormattedMessage>();

        if (response == null)
            return output;

        try
        {
            // Convert response to JsonElement for parsing
            JsonElement? rootElement = null;
            if (response is JsonElement element)
            {
                rootElement = element;
            }
            else if (response is string jsonString)
            {
                try
                {
                    using var doc = JsonDocument.Parse(jsonString);
                    rootElement = doc.RootElement;
                }
                catch (JsonException) { }
            }

            if (!rootElement.HasValue)
                return output;

            var root = rootElement.Value;

            // Handle chat completions format
            if (root.TryGetProperty("choices", out var choicesElement) && choicesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var choice in choicesElement.EnumerateArray())
                {
                    var content = new List<FormattedContentItem>();
                    var role = "assistant";

                    if (choice.TryGetProperty("message", out var messageElement))
                    {
                        if (messageElement.TryGetProperty("role", out var roleElement) && roleElement.ValueKind == JsonValueKind.String)
                        {
                            role = roleElement.GetString() ?? role;
                        }

                        if (messageElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                        {
                            var text = contentElement.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                content.Add(new FormattedContentItem
                                {
                                    Type = "text",
                                    Text = text
                                });
                            }
                        }

                        if (messageElement.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var toolCall in toolCallsElement.EnumerateArray())
                            {
                                if (toolCall.TryGetProperty("function", out var functionElement) &&
                                    functionElement.TryGetProperty("name", out var nameElement) &&
                                    nameElement.ValueKind == JsonValueKind.String)
                                {
                                    var functionCall = new FormattedFunctionCall
                                    {
                                        Name = nameElement.GetString() ?? ""
                                    };

                                    string? id = null;
                                    if (toolCall.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
                                    {
                                        id = idElement.GetString();
                                    }

                                    object arguments = "";
                                    if (functionElement.TryGetProperty("arguments", out var argsElement))
                                    {
                                        arguments = argsElement.ValueKind switch
                                        {
                                            JsonValueKind.String => argsElement.GetString() ?? "",
                                            JsonValueKind.Object => ConvertJsonElementToObject(argsElement),
                                            _ => argsElement.ToString()
                                        };
                                    }

                                    functionCall.Arguments = arguments;

                                    content.Add(new FormattedContentItem
                                    {
                                        Type = "function",
                                        Id = id,
                                        Function = functionCall
                                    });
                                }
                            }
                        }
                    }

                    if (content.Count > 0)
                    {
                        output.Add(new FormattedMessage
                        {
                            Role = role,
                            Content = content
                        });
                    }
                }
            }

            // Handle Responses API format
            if (root.TryGetProperty("output", out var outputElement) && outputElement.ValueKind == JsonValueKind.Array)
            {
                var content = new List<FormattedContentItem>();
                var role = "assistant";

                foreach (var item in outputElement.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
                    {
                        var type = typeElement.GetString();

                        if (type == "message")
                        {
                            if (item.TryGetProperty("role", out var roleElement) && roleElement.ValueKind == JsonValueKind.String)
                            {
                                role = roleElement.GetString() ?? role;
                            }

                            if (item.TryGetProperty("content", out var contentElement))
                            {
                                if (contentElement.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var contentItem in contentElement.EnumerateArray())
                                    {
                                        if (contentItem.TryGetProperty("type", out var contentTypeElement) && contentTypeElement.ValueKind == JsonValueKind.String)
                                        {
                                            var contentType = contentTypeElement.GetString();
                                            if (contentType == "output_text" || contentType == "text")
                                            {
                                                if (contentItem.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                                                {
                                                    content.Add(new FormattedContentItem
                                                    {
                                                        Type = "text",
                                                        Text = textElement.GetString()
                                                    });
                                                }
                                            }
                                            else if (contentType == "input_image")
                                            {
                                                if (contentItem.TryGetProperty("image_url", out var imageUrlElement) && imageUrlElement.ValueKind == JsonValueKind.String)
                                                {
                                                    content.Add(new FormattedContentItem
                                                    {
                                                        Type = "image",
                                                        Image = imageUrlElement.GetString()
                                                    });
                                                }
                                            }
                                        }
                                    }
                                }
                                else if (contentElement.ValueKind == JsonValueKind.String)
                                {
                                    content.Add(new FormattedContentItem
                                    {
                                        Type = "text",
                                        Text = contentElement.GetString()
                                    });
                                }
                            }
                        }
                        else if (type == "function_call")
                        {
                            var functionCall = new FormattedFunctionCall();

                            if (item.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
                            {
                                functionCall.Name = nameElement.GetString() ?? "";
                            }

                            object arguments = "";
                            if (item.TryGetProperty("arguments", out var argsElement))
                            {
                                arguments = argsElement.ValueKind switch
                                {
                                    JsonValueKind.String => argsElement.GetString() ?? "",
                                    JsonValueKind.Object => ConvertJsonElementToObject(argsElement),
                                    _ => argsElement.ToString()
                                };
                            }
                            functionCall.Arguments = arguments;

                            string? id = null;
                            if (item.TryGetProperty("call_id", out var callIdElement) && callIdElement.ValueKind == JsonValueKind.String)
                            {
                                id = callIdElement.GetString();
                            }
                            else if (item.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
                            {
                                id = idElement.GetString();
                            }

                            content.Add(new FormattedContentItem
                            {
                                Type = "function",
                                Id = id,
                                Function = functionCall
                            });
                        }
                    }
                }

                if (content.Count > 0)
                {
                    output.Add(new FormattedMessage
                    {
                        Role = role,
                        Content = content
                    });
                }
            }

            // Handle embeddings format (no output content)
            if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
            {
                // Embeddings don't have output content in JS SDK
                return output;
            }
        }
        catch (JsonException)
        {
            // Ignore formatting errors
        }
        catch (ArgumentException)
        {
            // Ignore argument errors
        }
        catch (InvalidOperationException)
        {
            // Ignore invalid operation errors
        }

        return output;
    }

    private static object ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal
                : element.TryGetInt64(out var longVal) ? longVal
                : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Array => element
                .EnumerateArray()
                .Select(ConvertJsonElementToObject)
                .ToList(),
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElementToObject(p.Value)),
            JsonValueKind.Undefined => null!,
            _ => element.GetRawText(),
        };
    }

    public static int CalculateWebSearchCount(object result)
    {
        if (result == null)
            return 0;

        try
        {
            JsonElement? rootElement = null;
            if (result is JsonElement element)
            {
                rootElement = element;
            }
            else if (result is string jsonString)
            {
                try
                {
                    using var doc = JsonDocument.Parse(jsonString);
                    rootElement = doc.RootElement;
                }
                catch (JsonException) { return 0; }
            }

            if (!rootElement.HasValue)
                return 0;

            var root = rootElement.Value;

            // Priority 1: Exact Count - OpenAI Responses API web_search_call items
            if (root.TryGetProperty("output", out var outputElement) && outputElement.ValueKind == JsonValueKind.Array)
            {
                int count = 0;
                foreach (var item in outputElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object && 
                        item.TryGetProperty("type", out var typeElement) && 
                        typeElement.ValueKind == JsonValueKind.String &&
                        typeElement.GetString() == "web_search_call")
                    {
                        count++;
                    }
                }
                if (count > 0)
                    return count;
            }

            // Priority 2: Binary Detection (1 or 0)

            // Check for citations at root level (Perplexity)
            if (root.TryGetProperty("citations", out var citationsElement) && 
                citationsElement.ValueKind == JsonValueKind.Array && 
                citationsElement.GetArrayLength() > 0)
            {
                return 1;
            }

            // Check for search_results at root level (Perplexity via OpenRouter)
            if (root.TryGetProperty("search_results", out var searchResultsElement) && 
                searchResultsElement.ValueKind == JsonValueKind.Array && 
                searchResultsElement.GetArrayLength() > 0)
            {
                return 1;
            }

            // Check for usage.search_context_size (Perplexity via OpenRouter)
            if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
            {
                if (usageElement.TryGetProperty("search_context_size", out var searchContextElement) &&
                    searchContextElement.ValueKind == JsonValueKind.Number &&
                    searchContextElement.GetInt32() > 0)
                {
                    return 1;
                }
            }

            // Check for annotations with url_citation in choices[].message or choices[].delta (OpenAI/Perplexity)
            if (root.TryGetProperty("choices", out var choicesElement) && choicesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var choice in choicesElement.EnumerateArray())
                {
                    JsonElement? content = null;
                    if (choice.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.Object)
                        content = messageElement;
                    else if (choice.TryGetProperty("delta", out var deltaElement) && deltaElement.ValueKind == JsonValueKind.Object)
                        content = deltaElement;

                    if (content.HasValue && content.Value.ValueKind == JsonValueKind.Object && 
                        content.Value.TryGetProperty("annotations", out var annotationsElement) &&
                        annotationsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ann in annotationsElement.EnumerateArray())
                        {
                            if (ann.TryGetProperty("type", out var annTypeElement) &&
                                annTypeElement.ValueKind == JsonValueKind.String &&
                                annTypeElement.GetString() == "url_citation")
                            {
                                return 1;
                            }
                        }
                    }
                }
            }

            // Check for annotations in output[].content[] (OpenAI Responses API)
            if (root.TryGetProperty("output", out var outputElement2) && outputElement2.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in outputElement2.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object && 
                        item.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var contentItem in contentElement.EnumerateArray())
                        {
                            if (contentItem.ValueKind == JsonValueKind.Object && 
                                contentItem.TryGetProperty("annotations", out var annotationsElement) &&
                                annotationsElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var ann in annotationsElement.EnumerateArray())
                                {
                                    if (ann.ValueKind == JsonValueKind.Object && 
                                        ann.TryGetProperty("type", out var annTypeElement) &&
                                        annTypeElement.ValueKind == JsonValueKind.String &&
                                        annTypeElement.GetString() == "url_citation")
                                    {
                                        return 1;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Check for grounding_metadata (Gemini)
            if (root.TryGetProperty("candidates", out var candidatesElement) && candidatesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var candidate in candidatesElement.EnumerateArray())
                {
                    if (candidate.ValueKind == JsonValueKind.Object && 
                        candidate.TryGetProperty("grounding_metadata", out var groundingElement) &&
                        groundingElement.ValueKind == JsonValueKind.Object)
                    {
                        return 1;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Ignore parsing errors
        }
        catch (ArgumentException)
        {
            // Ignore argument errors
        }
        catch (InvalidOperationException)
        {
            // Ignore invalid operation errors
        }

        return 0;
    }
}