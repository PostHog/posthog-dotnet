using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog.Json;
using PostHog.Library;
using PostHog.Versioning;
#if NETSTANDARD2_0 || NETSTANDARD2_1
#endif

namespace PostHog.Api;

/// <summary>
/// PostHog API client used to make API calls to PostHog for capturing events, feature flags, etc.
/// </summary>
internal sealed class PostHogApiClient : IDisposable
{
    internal const string LibraryName = "posthog-dotnet";

    readonly TimeProvider _timeProvider;
    readonly HttpClient _httpClient;
    readonly IOptions<PostHogOptions> _options;
    readonly ILogger<PostHogApiClient> _logger;

    /// <summary>
    /// Initialize a new PostHog client
    /// </summary>
    /// <param name="httpClient">The <see cref="HttpClient"/> used to make requests.</param>
    /// <param name="options">The options used to configure this client.</param>
    /// <param name="timeProvider">The time provider <see cref="TimeProvider"/> to use to determine time.</param>
    /// <param name="logger">The logger.</param>
    public PostHogApiClient(
        HttpClient httpClient,
        IOptions<PostHogOptions> options,
        TimeProvider timeProvider,
        ILogger<PostHogApiClient> logger)
    {
        _options = options;

        _timeProvider = timeProvider;

        _httpClient = httpClient;
        var framework = RuntimeInformation.FrameworkDescription;
        var os = RuntimeInformation.OSDescription;
        var arch = RuntimeInformation.ProcessArchitecture;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"{LibraryName}/{VersionConstants.Version} ({framework}; {os}; {arch})");

        logger.LogTraceApiClientCreated(HostUrl);
        _logger = logger;
    }

    Uri HostUrl => _options.Value.HostUrl;

    string ProjectApiKey => _options.Value.ProjectApiKey
                            ?? throw new InvalidOperationException("The Project API Key is not configured.");

    /// <summary>
    /// Capture an event with optional properties
    /// </summary>
    /// <param name="events">The events to send to PostHog.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    public async Task<ApiResult> CaptureBatchAsync(
        IEnumerable<CapturedEvent> events,
        CancellationToken cancellationToken)
    {
        var endpointUrl = new Uri(HostUrl, "batch");

        var payload = new Dictionary<string, object>
        {
            ["api_key"] = ProjectApiKey,
            ["historical_migrations"] = false,
            ["batch"] = events.ToReadOnlyList()
        };

        return await _httpClient.PostJsonWithRetryAsync<ApiResult>(
                   endpointUrl,
                   payload,
                   _timeProvider,
                   _options.Value,
                   cancellationToken)
               ?? new ApiResult(0);
    }

    /// <summary>
    /// Method to send an event to the PostHog API's /capture endpoint. This is used for
    /// capturing events, identify, alias, etc.
    /// </summary>
    public async Task<ApiResult> SendEventAsync(
        Dictionary<string, object> payload,
        CancellationToken cancellationToken)
    {
        PrepareAndMutatePayload(payload);

        var endpointUrl = new Uri(HostUrl, "capture");

        return await _httpClient.PostJsonAsync<ApiResult>(endpointUrl, payload, cancellationToken)
               ?? new ApiResult(0);
    }

    /// <summary>
    /// Retrieves all the feature flags for the user by making a request to the <c>/decide</c> endpoint.
    /// </summary>
    /// <param name="distinctUserId">The Id of the user.</param>
    /// <param name="personProperties">Optional: What person properties are known. Used to compute flags locally, if personalApiKey is present. Not needed if using remote evaluation, but can be used to override remote values for the purposes of feature flag evaluation.</param>
    /// <param name="groupProperties">Optional: What group properties are known. Used to compute flags locally, if personalApiKey is present.  Not needed if using remote evaluation, but can be used to override remote values for the purposes of feature flag evaluation.</param>
    /// <param name="flagKeysToEvaluate">The set of flag keys to evaluate. If empty, this returns all flags.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="DecideApiResult"/>.</returns>
    public async Task<DecideApiResult?> GetFeatureFlagsFromDecideAsync(
        string distinctUserId,
        Dictionary<string, object?>? personProperties,
        GroupCollection? groupProperties,
        IReadOnlyList<string>? flagKeysToEvaluate,
        CancellationToken cancellationToken)
    {
        var endpointUrl = new Uri(HostUrl, "decide?v=4");

        var payload = new Dictionary<string, object>
        {
            ["distinct_id"] = distinctUserId
        };

        if (personProperties is { Count: > 0 })
        {
            payload["person_properties"] = personProperties;
        }

        if (flagKeysToEvaluate is { Count: > 0 })
        {
            payload["flag_keys_to_evaluate"] = flagKeysToEvaluate;
        }

        groupProperties?.AddToPayload(payload);

        PrepareAndMutatePayload(payload);

        return await _httpClient.PostJsonAsync<DecideApiResult>(
            endpointUrl,
            payload,
            cancellationToken);
    }

    /// <summary>
    /// Retrieves all the feature flags for the project by making a request to the
    /// <c>/api/feature_flag/local_evaluation</c> endpoint. This requires that a Personal API Key is set in
    /// <see cref="PostHogOptions"/>.
    /// </summary>
    /// <param name="etag">Optional ETag from a previous request for conditional fetching.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="LocalEvaluationResponse"/> containing the feature flags and ETag.</returns>
    /// <exception cref="ApiException">Thrown when the API returns a <c>quota_limited</c> error.</exception>
    public async Task<LocalEvaluationResponse> GetFeatureFlagsForLocalEvaluationAsync(
        string? etag,
        CancellationToken cancellationToken)
    {
        var uriBuilder = new UriBuilder(new Uri(HostUrl, "/api/feature_flag/local_evaluation"))
        {
            Query = $"token={Uri.EscapeDataString(ProjectApiKey)}&send_cohorts"
        };
        try
        {
            return await GetAuthenticatedResponseWithETagAsync<LocalEvaluationApiResult>(
                uriBuilder.Uri.PathAndQuery,
                etag,
                cancellationToken);
        }
        catch (ApiException e) when (e.ErrorType is "quota_limited")
        {
            // We want the caller to handle it.
            throw;
        }
        catch (Exception e) when (e is not ArgumentException and not NullReferenceException)
        {
            _logger.LogErrorUnableToGetFeatureFlagsAndPayloads(e);
            return LocalEvaluationResponse.Failure();
        }
    }

    /// <summary>
    /// Retrieves a remote config payload by making a request to the <c>/remote_config/</c> endpoint.
    /// </summary>
    /// <param name="key">The config key.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns></returns>
    public async Task<JsonDocument?> GetRemoteConfigPayloadAsync(string key, CancellationToken cancellationToken)
    {
        var uriBuilder = new UriBuilder(new Uri(HostUrl, $"/api/projects/@current/feature_flags/{Uri.EscapeDataString(key)}/remote_config"))
        {
            Query = $"token={Uri.EscapeDataString(ProjectApiKey)}"
        };

        return await GetAuthenticatedResponseAsync<JsonDocument>(
            uriBuilder.Uri.PathAndQuery,
            cancellationToken);
    }

    async Task<T?> GetAuthenticatedResponseAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var options = _options.Value ?? throw new InvalidOperationException(nameof(_options));
        var personalApiKey = options.PersonalApiKey
                             ?? throw new InvalidOperationException(
                                 "This API requires that a Personal API Key is set.");

        var endpointUrl = new Uri(HostUrl, relativeUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, endpointUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme: "Bearer", personalApiKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        await response.EnsureSuccessfulApiCall(cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(
            JsonSerializerHelper.Options,
            cancellationToken);
    }

    async Task<LocalEvaluationResponse> GetAuthenticatedResponseWithETagAsync<T>(
        string relativeUrl,
        string? etag,
        CancellationToken cancellationToken)
        where T : LocalEvaluationApiResult
    {
        var options = _options.Value ?? throw new InvalidOperationException(nameof(_options));
        var personalApiKey = options.PersonalApiKey
                             ?? throw new InvalidOperationException(
                                 "This API requires that a Personal API Key is set.");

        var endpointUrl = new Uri(HostUrl, relativeUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, endpointUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme: "Bearer", personalApiKey);

        // Add If-None-Match header for conditional request if we have an ETag
        if (!string.IsNullOrEmpty(etag))
        {
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);

        // Get ETag from response (may be present even on 304)
        var responseETag = response.Headers.ETag?.Tag;

        // Handle 304 Not Modified - flags haven't changed
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            _logger.LogDebugFlagsNotModified();
            // Preserve the original ETag if the server didn't return one
            return LocalEvaluationResponse.NotModified(responseETag ?? etag);
        }

        await response.EnsureSuccessfulApiCall(cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<T>(
            JsonSerializerHelper.Options,
            cancellationToken);

        return LocalEvaluationResponse.Success(result, responseETag);
    }

    void PrepareAndMutatePayload(Dictionary<string, object> payload)
    {
        payload["api_key"] = ProjectApiKey;

        var properties = payload.GetOrAdd<string, Dictionary<string, object>>("properties");

        properties[PostHogProperties.Lib] = LibraryName;
        properties[PostHogProperties.LibVersion] = VersionConstants.Version;
        properties[PostHogProperties.Os] = RuntimeInformation.OSDescription;
        properties[PostHogProperties.Framework] = RuntimeInformation.FrameworkDescription;
        properties[PostHogProperties.Architecture] = RuntimeInformation.ProcessArchitecture.ToString();
        properties[PostHogProperties.GeoIpDisable] = properties.GetValueOrDefault(PostHogProperties.GeoIpDisable, true);

        properties.Merge(_options.Value.SuperProperties);

        payload["properties"] = properties;

        // Only set timestamp if one isn't already provided in properties
        if (!payload.ContainsKey("timestamp") && !properties.ContainsKey("timestamp"))
        {
            payload["timestamp"] = _timeProvider.GetUtcNow(); // ISO 8601
        }
    }

    /// <summary>
    /// Dispose of HttpClient
    /// </summary>
    public void Dispose() => _httpClient.Dispose();
}

internal static partial class PostHogApiClientLoggerExtensions
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Trace,
        Message = "Api Client Created: {HostUrl}")]
    public static partial void LogTraceApiClientCreated(this ILogger<PostHogApiClient> logger, Uri hostUrl);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Unable to retrieve remote config payload")]
    public static partial void LogErrorUnableToGetRemoteConfigPayload(
        this ILogger<PostHogApiClient> logger, Exception exception);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "[FEATURE FLAGS] Unable to get feature flags and payloads")]
    public static partial void LogErrorUnableToGetFeatureFlagsAndPayloads(
        this ILogger<PostHogApiClient> logger,
        Exception exception);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "[FEATURE FLAGS] Flags not modified (304), using cached data")]
    public static partial void LogDebugFlagsNotModified(this ILogger<PostHogApiClient> logger);
}