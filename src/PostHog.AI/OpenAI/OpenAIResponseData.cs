using System.Net;

namespace PostHog.AI.OpenAI;

public class OpenAIResponseData
{
    public string? Model { get; set; }
    public TokenUsage? Usage { get; set; }
    public bool HasOutput { get; set; }
    public bool IsStreaming { get; set; }
    public HttpStatusCode StatusCode { get; set; }
    public object? OutputContent { get; set; }
}
