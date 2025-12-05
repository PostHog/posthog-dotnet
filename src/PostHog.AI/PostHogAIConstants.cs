namespace PostHog.AI;

internal static class PostHogAIConstants
{
    // Event types
    public const string EventTypeGeneration = "$ai_generation";
    public const string EventTypeEmbedding = "$ai_embedding";

    // Property names
    public const string PropertyLib = "$ai_lib";
    public const string PropertyLibVersion = "$ai_lib_version";
    public const string PropertyProvider = "$ai_provider";
    public const string PropertyModel = "$ai_model";
    public const string PropertyHttpStatus = "$ai_http_status";
    public const string PropertyLatency = "$ai_latency";
    public const string PropertyTraceId = "$ai_trace_id";
    public const string PropertyBaseUrl = "$ai_base_url";
    public const string PropertyInput = "$ai_input";
    public const string PropertyOutputChoices = "$ai_output_choices";
    public const string PropertyModelParameters = "$ai_model_parameters";
    public const string PropertyInputTokens = "$ai_input_tokens";
    public const string PropertyOutputTokens = "$ai_output_tokens";
    public const string PropertyTotalTokens = "$ai_total_tokens";
    public const string PropertyReasoningTokens = "$ai_reasoning_tokens";
    public const string PropertyCacheReadInputTokens = "$ai_cache_read_input_tokens";
    public const string PropertyStreaming = "$ai_streaming";
    public const string PropertyIsError = "$ai_is_error";
    public const string PropertyError = "$ai_error";

    // Request parameter names
    public const string ParamDistinctId = "posthogDistinctId";
    public const string ParamTraceId = "posthogTraceId";
    public const string ParamPrivacyMode = "posthogPrivacyMode";
    public const string ParamModelOverride = "posthogModelOverride";
    public const string ParamProviderOverride = "posthogProviderOverride";
    public const string ParamCaptureImmediate = "posthogCaptureImmediate";
    public const string ParamProperties = "posthogProperties";
    public const string ParamGroups = "posthogGroups";

    // Default values
    public const string DefaultLibVersion = "1.0.0";
    public const string DefaultLibName = "posthog-dotnet-ai";
    public const string DefaultProvider = "openai";

    // Limits
    public const int MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
    public const int MaxResponseBodySize = 50 * 1024 * 1024; // 50MB
    public const int MaxStreamBufferSize = 5 * 1024 * 1024; // 5MB
    public const int MaxJsonDepth = 64;


    public const string OpenAINamedClient = "PostHogOpenAI";
    public const string AzureOpenAINamedClient = "PostHogAzureOpenAI";
}
