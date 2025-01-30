using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog.Config;
using PostHog.Json;
using PostHog.Library;
using PostHog.Versioning;

namespace PostHog.Api;

/// <inheritdoc cref="IPostHogApiClient" />
public sealed class PostHogApiClient : IPostHogApiClient
{
    internal const string LibraryName = "posthog-dotnet";

    readonly TimeProvider _timeProvider;
    readonly HttpClient _httpClient;
    readonly IOptions<PostHogOptions> _options;

    /// <summary>
    /// Initialize a new PostHog client
    /// </summary>
    /// <remarks>
    /// This constructor is used for dependency injection.
    /// </remarks>
    /// <param name="options">The options used to configure this client.</param>
    /// <param name="timeProvider">The time provider <see cref="TimeProvider"/> to use to determine time.</param>
    /// <param name="logger">The logger.</param>
    public PostHogApiClient(
        IOptions<PostHogOptions> options,
        TimeProvider timeProvider,
        ILogger<PostHogApiClient> logger)
        : this(
            CreateHttpClient(logger),
            options,
            timeProvider,
            logger)
    {
    }

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

        logger.LogTraceApiClientCreated(HostUrl);
    }

    static HttpClient CreateHttpClient(ILogger<PostHogApiClient> logger) =>
        new(
#pragma warning disable CA2000
            new LoggingHttpMessageHandler(logger)
#pragma warning restore CA2000
            {
                InnerHandler = new HttpClientHandler()
            });

    Uri HostUrl => _options.Value.HostUrl;

    string ProjectApiKey => _options.Value.ProjectApiKey
                            ?? throw new InvalidOperationException("The Project API Key is not configured.");

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public async Task<ApiResult> SendEventAsync(
        Dictionary<string, object> payload,
        CancellationToken cancellationToken)
    {
        payload = payload ?? throw new ArgumentNullException(nameof(payload));

        PrepareAndMutatePayload(payload);

        var endpointUrl = new Uri(HostUrl, "capture");

        return await _httpClient.PostJsonAsync<ApiResult>(endpointUrl, payload, cancellationToken)
               ?? new ApiResult(0);
    }

    /// <inheritdoc/>
    public async Task<DecideApiResult?> GetAllFeatureFlagsFromDecideAsync(
        string distinctUserId,
        Dictionary<string, object?>? personProperties,
        GroupCollection? groupProperties,
        CancellationToken cancellationToken)
    {
        var endpointUrl = new Uri(HostUrl, "decide?v=3");

        var payload = new Dictionary<string, object>
        {
            ["distinct_id"] = distinctUserId,
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

    /// <inheritdoc/>
    public Version Version => new(VersionConstants.Version);

    void PrepareAndMutatePayload(Dictionary<string, object> payload)
    {
        payload["api_key"] = ProjectApiKey;

        if (payload.GetValueOrDefault("properties") is Dictionary<string, object> properties)
        {
            properties["$lib"] = LibraryName;
            properties["$lib_version"] = VersionConstants.Version;
            properties["$geoip_disable"] = true;
        }

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