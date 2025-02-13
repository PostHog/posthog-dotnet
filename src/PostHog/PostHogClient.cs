using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PostHog.Api;
using PostHog.Config;
using PostHog.Features;
using PostHog.Json;
using PostHog.Library;
using PostHog.Versioning;
using static PostHog.Library.Ensure;

namespace PostHog;

/// <inheritdoc cref="IPostHogClient" />
public sealed class PostHogClient : IPostHogClient
{
    readonly AsyncBatchHandler<CapturedEvent> _asyncBatchHandler;
    readonly PostHogApiClient _apiClient;
    readonly LocalFeatureFlagsLoader _featureFlagsLoader;
    readonly IFeatureFlagCache _featureFlagsCache;
    readonly MemoryCache _featureFlagSentCache;
    readonly TimeProvider _timeProvider;
    readonly IOptions<PostHogOptions> _options;
    readonly ITaskScheduler _taskScheduler;
    readonly ILogger<PostHogClient> _logger;

    /// <summary>
    /// Constructs a <see cref="IPostHogClient"/> with the specified options.
    /// </summary>
    /// <param name="options">The options to use with this client</param>
    public PostHogClient(IOptions<PostHogOptions> options)
        : this(
            options,
            NullFeatureFlagCache.Instance,
            new SimpleHttpClientFactory(),
            new TaskRunTaskScheduler(),
            TimeProvider.System, NullLoggerFactory.Instance)
    {
    }

    /// <summary>
    /// Constructs a <see cref="PostHogClient"/>. This is the main class used to interact with PostHog.
    /// </summary>
    /// <param name="options">The options used to configure the client.</param>
    /// <param name="featureFlagsCache">Caches feature flags for a duration appropriate to the environment.</param>
    /// <param name="httpClientFactory">Creates <see cref="HttpClient"/> for making requests to PostHog's API.</param>
    /// <param name="taskScheduler">Used to run tasks on the background.</param>
    /// <param name="timeProvider">The time provider <see cref="TimeProvider"/> to use to determine time.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public PostHogClient(
        IOptions<PostHogOptions> options,
        IFeatureFlagCache featureFlagsCache,
        IHttpClientFactory httpClientFactory,
        ITaskScheduler taskScheduler,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _options = NotNull(options);
        _taskScheduler = NotNull(taskScheduler);
        _apiClient = new PostHogApiClient(
            NotNull(httpClientFactory).CreateClient(nameof(PostHogClient)),
            options,
            timeProvider,
            loggerFactory.CreateLogger<PostHogApiClient>()
        );
        _featureFlagsCache = featureFlagsCache;
        _asyncBatchHandler = new AsyncBatchHandler<CapturedEvent>(
            batch => _apiClient.CaptureBatchAsync(batch, CancellationToken.None),
            options,
            taskScheduler,
            timeProvider,
            loggerFactory.CreateLogger<AsyncBatchHandler<CapturedEvent>>());

        _featureFlagsLoader = new LocalFeatureFlagsLoader(
            _apiClient,
            options,
            taskScheduler,
            timeProvider,
            NullLogger.Instance);
        _featureFlagsCache = featureFlagsCache;
        _featureFlagSentCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = options.Value.FeatureFlagSentCacheSizeLimit,
            Clock = new TimeProviderSystemClock(NotNull(timeProvider)),
            CompactionPercentage = options.Value.FeatureFlagSentCacheCompactionPercentage
        });

        _timeProvider = timeProvider;
        _logger = loggerFactory.CreateLogger<PostHogClient>();
        _logger.LogInfoClientCreated(options.Value.MaxBatchSize, options.Value.FlushInterval, options.Value.FlushAt);
    }

    /// <summary>
    /// To marry up whatever a user does before they sign up or log in with what they do after you need to make an
    /// alias call. This will allow you to answer questions like "Which marketing channels leads to users churning
    /// after a month? or "What do users do on our website before signing up? In a purely back-end implementation, this
    /// means whenever an anonymous user does something, you'll want to send a session ID with the capture call.
    /// Then, when that users signs up, you want to do an alias call with the session ID and the newly created user ID.
    /// The same concept applies for when a user logs in. If you're using PostHog in the front-end and back-end,
    ///  doing the identify call in the frontend will be enough.
    /// </summary>
    /// <param name="previousId">The anonymous or temporary identifier you were using for the user.</param>
    /// <param name="newId">The identifier for the known user. Typically a user id in your database.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An <see cref="ApiResult"/> with the result of the operation.</returns>
    public async Task<ApiResult> AliasAsync(
        string previousId,
        string newId,
        CancellationToken cancellationToken)
        => await _apiClient.AliasAsync(previousId, newId, cancellationToken);

    /// <inheritdoc/>
    public async Task<ApiResult> IdentifyAsync(
        string distinctId,
        Dictionary<string, object>? personPropertiesToSet,
        Dictionary<string, object>? personPropertiesToSetOnce,
        CancellationToken cancellationToken)
        => await _apiClient.IdentifyAsync(
            distinctId,
            personPropertiesToSet,
            personPropertiesToSetOnce,
            cancellationToken);

    /// <inheritdoc/>
    public Task<ApiResult> GroupIdentifyAsync(
        string type,
        StringOrValue<int> key,
        Dictionary<string, object>? properties,
        CancellationToken cancellationToken)
    => _apiClient.GroupIdentifyAsync(type, key, properties, cancellationToken);

    /// <inheritdoc/>
    public bool Capture(
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        bool sendFeatureFlags)
    {
        var capturedEvent = new CapturedEvent(
            eventName,
            distinctId,
            properties,
            timestamp: _timeProvider.GetUtcNow());

        if (groups is { Count: > 0 })
        {
            capturedEvent.Properties["$groups"] = groups.ToDictionary(g => g.GroupType, g => g.GroupKey);
        }

        capturedEvent.Properties.Merge(_options.Value.SuperProperties);

        var batchTask = sendFeatureFlags
            ? AddFreshFeatureFlagDataAsync(distinctId, groups, capturedEvent)
            : _featureFlagsLoader.IsLoaded && eventName != "$feature_flag_called"
                ? AddLocalFeatureFlagDataAsync(distinctId, groups, capturedEvent)
                : Task.FromResult(capturedEvent);

        if (_asyncBatchHandler.Enqueue(batchTask))
        {
            _logger.LogTraceCaptureCalled(eventName, capturedEvent.Properties.Count, _asyncBatchHandler.Count);
            return true;
        }
        _logger.LogWarnCaptureFailed(eventName, capturedEvent.Properties.Count, _asyncBatchHandler.Count);
        return false;
    }

    async Task<CapturedEvent> AddFreshFeatureFlagDataAsync(
        string distinctId,
        GroupCollection? groups,
        CapturedEvent capturedEvent)
    {
        var flags = await DecideAsync(
            distinctId,
            options: new AllFeatureFlagsOptions
            {
                Groups = groups
            },
            CancellationToken.None);

        return AddFeatureFlagsToCapturedEvent(capturedEvent, flags);
    }

    async Task<CapturedEvent> AddLocalFeatureFlagDataAsync(
        string distinctId,
        GroupCollection? groups,
        CapturedEvent capturedEvent)
    {
        var flags = await GetAllFeatureFlagsAsync(
            distinctId,
            options: new AllFeatureFlagsOptions
            {
                Groups = groups,
                OnlyEvaluateLocally = true
            },
            CancellationToken.None);

        return AddFeatureFlagsToCapturedEvent(capturedEvent, flags);
    }

    static CapturedEvent AddFeatureFlagsToCapturedEvent(
        CapturedEvent capturedEvent,
        IReadOnlyDictionary<string, FeatureFlag> flags)
    {
        capturedEvent.Properties.Merge(flags.ToDictionary(
            f => $"$feature/{f.Key}",
            f => f.Value.ToResponseObject()));
        capturedEvent.Properties["$active_feature_flags"] = flags
            .Where(f => (bool)f.Value)
            .Select(kvp => kvp.Key)
            .ToArray();
        return capturedEvent;
    }

    /// <inheritdoc/>
    public async Task<bool?> IsFeatureEnabledAsync(
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options,
        CancellationToken cancellationToken)
    {
        var result = await GetFeatureFlagAsync(
            featureKey,
            distinctId,
            options,
            cancellationToken);

        return result?.IsEnabled;
    }

    /// <inheritdoc/>
    public async Task<FeatureFlag?> GetFeatureFlagAsync(
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options,
        CancellationToken cancellationToken)
    {
        var localEvaluator = await _featureFlagsLoader.GetFeatureFlagsForLocalEvaluationAsync(cancellationToken);
        FeatureFlag? response = null;
        if (localEvaluator is not null && localEvaluator.TryGetLocalFeatureFlag(featureKey, out var localFeatureFlag))
        {
            try
            {
                var value = localEvaluator.ComputeFlagLocally(
                    localFeatureFlag,
                    distinctId,
                    options?.Groups ?? [],
                    options?.PersonProperties ?? []);
                response = FeatureFlag.CreateFromLocalEvaluation(featureKey, value, localFeatureFlag);
                _logger.LogDebugSuccessLocally(featureKey, response);
            }
            catch (InconclusiveMatchException e)
            {
                _logger.LogDebugFailedToComputeFlag(e, featureKey);
            }
            catch (Exception e) when (e is HttpRequestException or UnauthorizedAccessException)
            {
                _logger.LogErrorFailedToComputeFlag(e, featureKey);
            }
        }

        var flagWasLocallyEvaluated = response is not null;
        if (!flagWasLocallyEvaluated && options is not { OnlyEvaluateLocally: true })
        {
            try
            {
                // Fallback to Decide
                var flags = await DecideAsync(
                    distinctId,
                    options ?? new FeatureFlagOptions(),
                    cancellationToken);

                response = flags.GetValueOrDefault(featureKey) ?? new FeatureFlag
                {
                    Key = featureKey,
                    IsEnabled = false
                };
                _logger.LogDebugSuccessRemotely(featureKey, response);
            }
            catch (Exception e) when (e is not ArgumentException and not NullReferenceException)
            {
                _logger.LogErrorUnableToGetRemotely(e, featureKey);
            }
        }

        options ??= new FeatureFlagOptions(); // We need the defaults if options is null.

        if (options.SendFeatureFlagEvents)
        {
            _featureFlagSentCache.GetOrCreate(
                key: (distinctId, featureKey, (string)response),
                // This is only called if the key doesn't exist in the cache.
                factory: cacheEntry => CaptureFeatureFlagSentEvent(
                    distinctId,
                    featureKey,
                    cacheEntry,
                    response,
                    options.Groups));
        }

        if (_featureFlagSentCache.Count >= _options.Value.FeatureFlagSentCacheSizeLimit)
        {
            // We need to fire and forget the compaction because it can be expensive.
            _taskScheduler.Run(
                () => _featureFlagSentCache.Compact(_options.Value.FeatureFlagSentCacheCompactionPercentage),
                cancellationToken);
        }

        return response;
    }

    bool CaptureFeatureFlagSentEvent(
        string distinctId,
        string featureKey,
        ICacheEntry cacheEntry,
        FeatureFlag? flag,
        GroupCollection? groupProperties)
    {
        cacheEntry.SetSize(1); // Each entry has a size of 1
        cacheEntry.SetPriority(CacheItemPriority.Low);
        cacheEntry.SetSlidingExpiration(_options.Value.FeatureFlagSentCacheSlidingExpiration);

        Capture(
            distinctId,
            eventName: "$feature_flag_called",
            properties: new Dictionary<string, object>
            {
                ["$feature_flag"] = featureKey,
                ["$feature_flag_response"] = flag.ToResponseObject(),
                ["locally_evaluated"] = false,
                [$"$feature/{featureKey}"] = flag.ToResponseObject()
            },
            groups: groupProperties,
            sendFeatureFlags: false);

        return true;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, FeatureFlag>> GetAllFeatureFlagsAsync(
        string distinctId,
        AllFeatureFlagsOptions? options,
        CancellationToken cancellationToken)
    {
        if (_options.Value.PersonalApiKey is not null)
        {

            // Attempt to load local feature flags.
            var localEvaluator = await _featureFlagsLoader.GetFeatureFlagsForLocalEvaluationAsync(cancellationToken);
            if (localEvaluator is not null)
            {
                var (localEvaluationResults, fallbackToDecide) = localEvaluator.EvaluateAllFlags(
                    distinctId,
                    options?.Groups,
                    options?.PersonProperties,
                    warnOnUnknownGroups: false);

                if (!fallbackToDecide || options is { OnlyEvaluateLocally: true })
                {
                    return localEvaluationResults;
                }
            }
        }

        try
        {
            return await DecideAsync(distinctId, options, cancellationToken);
        }
        catch (Exception e) when (e is not ArgumentException and not NullReferenceException)
        {
            _logger.LogErrorUnableToGetFeatureFlagsAndPayloads(e);
            return new Dictionary<string, FeatureFlag>();
        }
    }

    // Retrieves all the evaluated feature flags from the /decide endpoint.
    async Task<IReadOnlyDictionary<string, FeatureFlag>> DecideAsync(
        string distinctId,
        AllFeatureFlagsOptions? options,
        CancellationToken cancellationToken)
    {
        return await _featureFlagsCache.GetAndCacheFeatureFlagsAsync(
            distinctId,
            fetcher: _ => FetchDecideAsync(),
            cancellationToken: cancellationToken);

        async Task<IReadOnlyDictionary<string, FeatureFlag>> FetchDecideAsync()
        {
            var results = await _apiClient.GetAllFeatureFlagsFromDecideAsync(
                distinctId,
                options?.PersonProperties,
                options?.Groups,
                cancellationToken);

            return results?.FeatureFlags is not null
                ? results.FeatureFlags.ToReadOnlyDictionary(
                    kvp => kvp.Key,
                    kvp => FeatureFlag.CreateFromDecide(kvp.Key, kvp.Value, results))
                : new Dictionary<string, FeatureFlag>();
        }
    }

    /// <inheritdoc/>
    public async Task FlushAsync() => await _asyncBatchHandler.FlushAsync();

    /// <inheritdoc/>
    public string Version => VersionConstants.Version;

    /// <inheritdoc/>
    public void Dispose() => DisposeAsync().AsTask().Wait();

    /// <summary>
    /// Clears the local flags cache.
    /// </summary>
    public void ClearLocalFlagsCache() => _featureFlagsLoader.Clear();

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Stop the polling and wait for it.
        await _asyncBatchHandler.DisposeAsync();
        _apiClient.Dispose();
        _featureFlagSentCache.Dispose();
        _featureFlagsLoader.Dispose();
    }
}

internal static partial class PostHogClientLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "PostHog Client created with Max Batch Size: {MaxBatchSize}, Flush Interval: {FlushInterval}, and FlushAt: {FlushAt}")]
    public static partial void LogInfoClientCreated(
        this ILogger<PostHogClient> logger,
        int maxBatchSize,
        TimeSpan flushInterval,
        int flushAt);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Trace,
        Message = "Capture called for event {EventName} with {PropertiesCount} properties. {Count} items in the queue")]
    public static partial void LogTraceCaptureCalled(
        this ILogger<PostHogClient> logger,
        string eventName,
        int propertiesCount,
        int count);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Debug,
        Message = "Failed to compute flag {Key} locally.")]
    public static partial void LogDebugFailedToComputeFlag(this ILogger<PostHogClient> logger, Exception e, string key);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Error,
        Message = "[FEATURE FLAGS] Error while computing variant locally for flag {Key}.")]
    public static partial void LogErrorFailedToComputeFlag(this ILogger<PostHogClient> logger, Exception e, string key);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "[FEATURE FLAGS] Unable to get flag {Key} remotely")]
    public static partial void LogErrorUnableToGetRemotely(this ILogger<PostHogClient> logger, Exception e, string key);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Debug,
        Message = "Successfully computed flag locally: {Key} -> {Result}.")]
    public static partial void LogDebugSuccessLocally(this ILogger<PostHogClient> logger, string key, FeatureFlag? result);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Debug,
        Message = "Successfully computed flag remotely: {Key} -> {Result}.")]
    public static partial void LogDebugSuccessRemotely(this ILogger<PostHogClient> logger, string key, FeatureFlag? result);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Warning,
        Message = "Capture failed for event {EventName} with {PropertiesCount} properties. {Count} items in the queue")]
    public static partial void LogWarnCaptureFailed(
        this ILogger<PostHogClient> logger,
        string eventName,
        int propertiesCount,
        int count);

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "[FEATURE FLAGS] You have to specify a personal_api_key to fetch decrypted feature flag payloads.")]
    public static partial void LogWarningPersonalApiKeyRequiredForFeatureFlagPayload(this ILogger<PostHogClient> logger);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Error,
        Message = "[FEATURE FLAGS] Error while fetching decrypted feature flag payload.")]
    public static partial void LogErrorUnableToGetDecryptedFeatureFlagPayload(
        this ILogger<PostHogClient> logger,
        Exception exception);
}