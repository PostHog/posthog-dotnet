using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace PostHog.AI.OpenAI;

internal partial class OpenAIRequestParser
{
    private readonly ILogger _logger;
    private readonly PostHogAIOptions _options;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public OpenAIRequestParser(
        ILogger logger,
        IOptions<PostHogAIOptions> options,
        JsonSerializerOptions? jsonSerializerOptions = null
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _jsonSerializerOptions =
            jsonSerializerOptions
            ?? new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                MaxDepth = PostHogAIConstants.MaxJsonDepth,
            };
    }

    public OpenAIRequestData ParseOpenAIRequest(
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
                _logger.FailedToParseOpenAIRequestJson(ex);
            }
#pragma warning restore CA1031
        }

        return data;
    }

    public static object ConvertJsonElementToObject(JsonElement element)
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

    public PosthogParams ExtractPosthogParams(
        HttpRequestMessage request,
        Dictionary<string, object>? requestJson
    )
    {
        var paramsDict = new PosthogParams();

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
                    _logger.FailedToSetPostHogParameter(ex, kvp.Key);
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
}

internal static partial class OpenAIRequestParserLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Failed to parse OpenAI request JSON")]
    public static partial void FailedToParseOpenAIRequestJson(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Failed to set PostHog parameter {Parameter}")]
    public static partial void FailedToSetPostHogParameter(this ILogger logger, Exception ex, string parameter);
}
