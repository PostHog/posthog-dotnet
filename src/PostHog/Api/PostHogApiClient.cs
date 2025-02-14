using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog.Config;
using PostHog.Features;
using PostHog.Json;
using PostHog.Library;
using PostHog.Versioning;
#if NETSTANDARD2_0 || NETSTANDARD2_1
#endif

namespace PostHog.Api;

/// <summary>
/// PostHog API client for capturing events and managing user tracking
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

        return await _httpClient.PostJsonAsync<ApiResult>(endpointUrl, payload, cancellationToken)
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
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="DecideApiResult"/>.</returns>
    public async Task<DecideApiResult?> GetAllFeatureFlagsFromDecideAsync(
        string distinctUserId,
        Dictionary<string, object?>? personProperties,
        GroupCollection? groupProperties,
        CancellationToken cancellationToken)
    {
        var endpointUrl = new Uri(HostUrl, "decide?v=3");

        var payload = new Dictionary<string, object>
        {
            ["distinct_id"] = distinctUserId
        };

        if (personProperties is { Count: > 0 })
        {
            payload["person_properties"] = personProperties;
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
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="LocalEvaluationApiResult"/> containing all the feature flags.</returns>
    public async Task<LocalEvaluationApiResult?> GetFeatureFlagsForLocalEvaluationAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value ?? throw new InvalidOperationException(nameof(_options));
        try
        {
            return await GetAuthenticatedResponseAsync<LocalEvaluationApiResult>(
                $"/api/feature_flag/local_evaluation/?token={options.ProjectApiKey}&send_cohorts",
                cancellationToken);
        }
        catch (Exception e) when (e is not ArgumentException and not NullReferenceException)
        {
            _logger.LogErrorUnableToGetFeatureFlagsAndPayloads(e);
            return null;
        }
    }

    /// <summary>
    /// Retrieves a remote config payload by making a request to the <c>/remote_config/</c> endpoint.
    /// </summary>
    /// <param name="key">The config key.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns></returns>
    public async Task<JsonDocument?> GetRemoteConfigPayloadAsync(string key, CancellationToken cancellationToken) =>
        await GetAuthenticatedResponseAsync<JsonDocument>(
            $"/api/projects/@current/feature_flags/{key}/remote_config/",
            cancellationToken);

    async Task<T?> GetAuthenticatedResponseAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var options = _options.Value ?? throw new InvalidOperationException(nameof(_options));
        var personalApiKey = options.PersonalApiKey
            ?? throw new InvalidOperationException("This API requires that a Personal API Key is set.");

        var endpointUrl = new Uri(HostUrl, relativeUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, endpointUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme: "Bearer", personalApiKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            try
            {
                var error = await response.Content.ReadFromJsonAsync<UnauthorizedApiResult>(
                    cancellationToken: cancellationToken);
                throw new UnauthorizedAccessException(error?.Detail ?? "Unauthorized");
            }
            // Get defensive here because I'm not sure that `Attr` is always a string, but I believe it be so.
            catch (JsonException e)
            {
                throw new UnauthorizedAccessException("Unauthorized. Could not deserialize the response for more info.", e);
            }
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(
            JsonSerializerHelper.Options,
            cancellationToken);
    }

    void PrepareAndMutatePayload(Dictionary<string, object> payload)
    {
        payload["api_key"] = ProjectApiKey;

        var properties = payload.GetOrAdd<string, Dictionary<string, object>>("properties");

        properties["$lib"] = LibraryName;
        properties["$lib_version"] = VersionConstants.Version;
        properties["$os"] = RuntimeInformation.OSDescription;
        properties["$framework"] = RuntimeInformation.FrameworkDescription;
        properties["$arch"] = RuntimeInformation.ProcessArchitecture.ToString();
        properties["$geoip_disable"] = true;

        properties.Merge(_options.Value.SuperProperties);

        payload["properties"] = properties;
        payload["timestamp"] = _timeProvider.GetUtcNow(); // ISO 8601
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
    public static partial void LogErrorUnableToGetFeatureFlagsAndPayloads(this ILogger<PostHogApiClient> logger, Exception exception);
}