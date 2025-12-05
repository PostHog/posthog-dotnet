using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog.AI.Utils;

namespace PostHog.AI.OpenAI;

internal class AIEventSender
{
    private readonly IPostHogClient _postHogClient;
    private readonly ILogger _logger;
    private readonly PostHogAIOptions _options;

    private static readonly Action<ILogger, Exception?> _failedToSendEvent = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(9, "FailedToSendEvent"),
        "Failed to send AI event to PostHog"
    );

    public AIEventSender(
        IPostHogClient postHogClient,
        ILogger logger,
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
    }

    public async Task SendAIEventAsync(
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
                [PostHogAIConstants.PropertyLib] = PostHogAIConstants.DefaultLibName,
                [PostHogAIConstants.PropertyLibVersion] = PostHogAIConstants.DefaultLibVersion,
                [PostHogAIConstants.PropertyProvider] =
                    posthogParams.ProviderOverride ?? PostHogAIConstants.DefaultProvider,
                [PostHogAIConstants.PropertyModel] =
                    posthogParams.ModelOverride
                    ?? responseData?.Model
                    ?? requestData.Model
                    ?? "unknown",
                [PostHogAIConstants.PropertyHttpStatus] = (int)statusCode,
                [PostHogAIConstants.PropertyLatency] = latency,
                [PostHogAIConstants.PropertyTraceId] = posthogParams.TraceId,
                [PostHogAIConstants.PropertyBaseUrl] =
                    requestData.RequestUri?.GetLeftPart(UriPartial.Authority) ?? "",
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
            properties[PostHogAIConstants.PropertyInput] = Sanitizer.Sanitize(inputContent)!;

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
            properties[PostHogAIConstants.PropertyOutputChoices] = Sanitizer.Sanitize(
                outputContent
            )!;

            // Add model parameters if available
            if (requestData.ModelParameters?.Count > 0)
            {
                properties[PostHogAIConstants.PropertyModelParameters] =
                    requestData.ModelParameters;
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
                    properties[PostHogAIConstants.PropertyInputTokens] = responseData
                        .Usage
                        .InputTokens
                        .Value;
                }

                if (responseData.Usage.OutputTokens.HasValue)
                {
                    properties[PostHogAIConstants.PropertyOutputTokens] = responseData
                        .Usage
                        .OutputTokens
                        .Value;
                }

                if (responseData.Usage.TotalTokens.HasValue)
                {
                    properties[PostHogAIConstants.PropertyTotalTokens] = responseData
                        .Usage
                        .TotalTokens
                        .Value;
                }

                if (responseData.Usage.ReasoningTokens.HasValue)
                {
                    properties[PostHogAIConstants.PropertyReasoningTokens] = responseData
                        .Usage
                        .ReasoningTokens
                        .Value;
                }

                if (responseData.Usage.CacheReadInputTokens.HasValue)
                {
                    properties[PostHogAIConstants.PropertyCacheReadInputTokens] = responseData
                        .Usage
                        .CacheReadInputTokens
                        .Value;
                }
            }

            // Add streaming flag if applicable
            if (responseData?.IsStreaming == true)
            {
                properties[PostHogAIConstants.PropertyStreaming] = true;
            }

            // Add error information if applicable
            if (exception != null)
            {
                properties[PostHogAIConstants.PropertyIsError] = true;
                properties[PostHogAIConstants.PropertyError] = exception.Message;
            }
            // Add error flag for non-success HTTP status codes
            else if ((int)statusCode >= 400)
            {
                properties[PostHogAIConstants.PropertyIsError] = true;
                properties[PostHogAIConstants.PropertyError] = $"HTTP {(int)statusCode}";
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
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _failedToSendEvent(_logger, ex);
        }
#pragma warning restore CA1031
    }
}
