namespace PostHog.AI;

/// <summary>
/// Constants for PostHog AI event field names.
/// These constants are shared across all AI provider integrations (OpenAI, Anthropic, Gemini, etc.).
/// </summary>
public static class PostHogAIFieldNames
{
    // Event names
    /// <summary>
    /// Event name for AI generation requests (chat completions, text completions, etc.).
    /// </summary>
    public const string Generation = "$ai_generation";

    /// <summary>
    /// Event name for AI embedding requests.
    /// </summary>
    public const string Embedding = "$ai_embedding";

    // Basic properties
    /// <summary>
    /// AI service provider (e.g., "openai", "anthropic", "gemini").
    /// </summary>
    public const string Provider = "$ai_provider";

    /// <summary>
    /// Library identifier (e.g., "posthog-dotnet").
    /// </summary>
    public const string Lib = "$ai_lib";

    /// <summary>
    /// Request latency in seconds.
    /// </summary>
    public const string Latency = "$ai_latency";

    /// <summary>
    /// Base URL of the API endpoint.
    /// </summary>
    public const string BaseUrl = "$ai_base_url";

    /// <summary>
    /// Full URL of the API request.
    /// </summary>
    public const string RequestUrl = "$ai_request_url";

    /// <summary>
    /// HTTP status code of the response.
    /// </summary>
    public const string HttpStatus = "$ai_http_status";

    // Model properties
    /// <summary>
    /// Model name used for the generation or embedding.
    /// </summary>
    public const string Model = "$ai_model";

    /// <summary>
    /// Dictionary of model parameters (top_p, frequency_penalty, presence_penalty, stop, etc.).
    /// </summary>
    public const string ModelParameters = "$ai_model_parameters";

    // Request parameters
    /// <summary>
    /// Temperature parameter used in the request.
    /// </summary>
    public const string Temperature = "$ai_temperature";

    /// <summary>
    /// Maximum tokens setting.
    /// </summary>
    public const string MaxTokens = "$ai_max_tokens";

    /// <summary>
    /// Whether the response was streamed.
    /// </summary>
    public const string Stream = "$ai_stream";

    /// <summary>
    /// Tools/functions available to the model.
    /// </summary>
    public const string Tools = "$ai_tools";

    // Input/Output
    /// <summary>
    /// Input messages, prompt, or input data.
    /// </summary>
    public const string Input = "$ai_input";

    /// <summary>
    /// Response choices from the LLM (for generation events).
    /// </summary>
    public const string OutputChoices = "$ai_output_choices";

    // Token usage
    /// <summary>
    /// Number of tokens in the input.
    /// </summary>
    public const string InputTokens = "$ai_input_tokens";

    /// <summary>
    /// Number of tokens in the output.
    /// </summary>
    public const string OutputTokens = "$ai_output_tokens";

    /// <summary>
    /// Total number of tokens used.
    /// </summary>
    public const string TotalTokens = "$ai_total_tokens";

    /// <summary>
    /// Number of tokens read from cache (when available).
    /// </summary>
    public const string CacheReadInputTokens = "$ai_cache_read_input_tokens";

    /// <summary>
    /// Number of tokens written to cache (when available).
    /// </summary>
    public const string CacheCreationInputTokens = "$ai_cache_creation_input_tokens";

    // Tracing and context
    /// <summary>
    /// Unique identifier (UUID) for grouping AI events.
    /// </summary>
    public const string TraceId = "$ai_trace_id";

    /// <summary>
    /// Groups related traces together.
    /// </summary>
    public const string SessionId = "$ai_session_id";

    /// <summary>
    /// Unique identifier for this generation.
    /// </summary>
    public const string SpanId = "$ai_span_id";

    /// <summary>
    /// Name given to this generation.
    /// </summary>
    public const string SpanName = "$ai_span_name";

    /// <summary>
    /// Parent span ID for tree view grouping.
    /// </summary>
    public const string ParentId = "$ai_parent_id";

    // Error handling
    /// <summary>
    /// Boolean indicating if the request was an error.
    /// </summary>
    public const string IsError = "$ai_is_error";

    /// <summary>
    /// Error message or object.
    /// </summary>
    public const string Error = "$ai_error";
}


