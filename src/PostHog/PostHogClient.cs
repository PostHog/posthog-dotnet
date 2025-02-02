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

    /// <inheritdoc/>
    public async Task<ApiResult> IdentifyPersonAsync(
        string distinctId,
        Dictionary<string, object>? personPropertiesToSet,
        Dictionary<string, object>? personPropertiesToSetOnce,
        CancellationToken cancellationToken)
        => await _apiClient.IdentifyPersonAsync(
            distinctId,
            personPropertiesToSet,
            personPropertiesToSetOnce,
            cancellationToken);

    /// <inheritdoc/>
    public Task<ApiResult> IdentifyGroupAsync(
        string type,
        StringOrValue<int> key,
        Dictionary<string, object>? properties,
        CancellationToken cancellationToken)
    => _apiClient.IdentifyGroupAsync(type, key, properties, cancellationToken);

    /// <inheritdoc/>
    public bool CaptureEvent(
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

        if (_asyncBatchHandler.Enqueue(capturedEvent))
        {
            _logger.LogTraceCaptureCalled(eventName, capturedEvent.Properties.Count, _asyncBatchHandler.Count);
            return true;
        }
        _logger.LogWarnCaptureFailed(eventName, capturedEvent.Properties.Count, _asyncBatchHandler.Count);
        return false;
    }

    /// <inheritdoc/>
    public async Task<bool?> IsFeatureEnabledAsync(
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options,
        CancellationToken cancellationToken)
    {
        var result = await GetFeatureFlagAsync(featureKey,
            distinctId,
            options, cancellationToken);

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
                response = new FeatureFlag(
                    featureKey,
                    localEvaluator.ComputeFlagLocally(
                        localFeatureFlag,
                        distinctId,
                        options?.GroupProperties ?? [],
                        options?.PersonProperties ?? []),
                    localFeatureFlag.Filters?.Payloads);
                _logger.LogDebugSuccessLocally(featureKey, response);
            }
            catch (InconclusiveMatchException e)
            {
                _logger.LogDebugFailedToComputeFlag(e, featureKey);
            }
            catch (HttpRequestException e)
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

                response = flags.GetValueOrDefault(featureKey) ?? new FeatureFlag(featureKey, IsEnabled: false);
                _logger.LogDebugSuccessRemotely(featureKey, response);
            }
            catch (HttpRequestException e)
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
                    options.GroupProperties));
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

        CaptureEvent(
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
        if (_options.Value.PersonalApiKey is null)
        {
            return await DecideAsync(distinctId, options: options, cancellationToken);
        }

        // Attempt to load local feature flags.
        var localEvaluator = await _featureFlagsLoader.GetFeatureFlagsForLocalEvaluationAsync(cancellationToken);
        if (localEvaluator is null)
        {
            return await DecideAsync(distinctId, options: options, cancellationToken);
        }

        var (localEvaluationResults, fallbackToDecide) = localEvaluator.EvaluateAllFlags(
            distinctId,
            options?.GroupProperties,
            options?.PersonProperties,
            warnOnUnknownGroups: false);

        if (!fallbackToDecide || options is { OnlyEvaluateLocally: true })
        {
            return localEvaluationResults;
        }

        return await DecideAsync(distinctId, options: options, cancellationToken);
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
                options?.GroupProperties,
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
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Capture failed for event {EventName} with {PropertiesCount} properties. {Count} items in the queue")]
    public static partial void LogWarnCaptureFailed(
        this ILogger<PostHogClient> logger,
        string eventName,
        int propertiesCount,
        int count);
}