using System.Net;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace PostHog.AI.OpenAI;

public class StreamingToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "";
}

public class OpenAIResponseData
{
    public string? Model { get; set; }
    public TokenUsage? Usage { get; set; }
    public bool HasOutput { get; set; }
    public bool IsStreaming { get; set; }
    public HttpStatusCode StatusCode { get; set; }
    public object? OutputContent { get; set; }
    public IReadOnlyList<FormattedMessage>? OutputFormatted { get; private set; }
    
    // For streaming accumulation
    internal StringBuilder? AccumulatedContent { get; set; }
    internal Dictionary<int, StreamingToolCall>? ToolCallsInProgress { get; set; }

    internal void SetOutputFormatted(IReadOnlyList<FormattedMessage>? formatted)
    {
        OutputFormatted = formatted;
    }
    
    internal void AddAccumulatedContent(string content)
    {
        (AccumulatedContent ??= new StringBuilder()).Append(content);
    }
    
    internal void AddOrUpdateToolCall(int index, string? id, string? name, string? arguments)
    {
        ToolCallsInProgress ??= new Dictionary<int, StreamingToolCall>();
        
        if (!ToolCallsInProgress.TryGetValue(index, out var toolCall))
        {
            toolCall = new StreamingToolCall();
            ToolCallsInProgress[index] = toolCall;
        }
        
        if (!string.IsNullOrEmpty(id))
        {
            toolCall.Id = id;
        }
        if (!string.IsNullOrEmpty(name))
        {
            toolCall.Name = name;
        }
        if (!string.IsNullOrEmpty(arguments))
        {
            toolCall.Arguments += arguments;
        }
    }
    
    internal void BuildFormattedOutputFromStreaming()
    {
        var messages = new List<FormattedMessage>();
        var contentItems = new List<FormattedContentItem>();
        
        // Add text content if any
        if (AccumulatedContent?.Length > 0)
        {
            contentItems.Add(new FormattedContentItem
            {
                Type = "text",
                Text = AccumulatedContent.ToString()
            });
        }
        
        // Add tool calls if any
        if (ToolCallsInProgress != null)
        {
            foreach (var kvp in ToolCallsInProgress)
            {
                var toolCall = kvp.Value;
                if (!string.IsNullOrEmpty(toolCall.Name))
                {
                    contentItems.Add(new FormattedContentItem
                    {
                        Type = "function",
                        Id = toolCall.Id,
                        Function = new FormattedFunctionCall
                        {
                            Name = toolCall.Name,
                            Arguments = toolCall.Arguments
                        }
                    });
                }
            }
        }
        
        if (contentItems.Count > 0)
        {
            messages.Add(new FormattedMessage
            {
                Role = "assistant",
                Content = contentItems
            });
            SetOutputFormatted(messages);
        }
    }
}
