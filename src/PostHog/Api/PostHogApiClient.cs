using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog.Config;
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
        var personalApiKey = _options.Value.PersonalApiKey
            ?? throw new InvalidOperationException("This API requires that a Personal API Key is set.");
        var options = _options.Value ?? throw new InvalidOperationException(nameof(_options));

        var endpointUrl = new Uri(HostUrl, $"/api/feature_flag/local_evaluation/?token={options.ProjectApiKey}&send_cohorts");

        using var request = new HttpRequestMessage(HttpMethod.Get, endpointUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme: "Bearer", personalApiKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        return await response.Content.ReadFromJsonAsync<LocalEvaluationApiResult>(
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
        EventId = 1,
        Level = LogLevel.Trace,
        Message = "Api Client Created: {HostUrl}")]
    public static partial void LogTraceApiClientCreated(this ILogger<PostHogApiClient> logger, Uri hostUrl);
}