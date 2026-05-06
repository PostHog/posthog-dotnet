using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PostHog.Api;
using PostHog.ErrorTracking;
using PostHog.Exceptions;
using PostHog.Features;
using PostHog.Json;
using PostHog.Library;
using PostHog.Versioning;
using static PostHog.Library.Ensure;

namespace PostHog;

/// <inheritdoc cref="IPostHogClient" />
public sealed class PostHogClient : IPostHogClient
{
    readonly AsyncBatchHandler<CapturedEvent, CapturedEventBatchContext> _asyncBatchHandler;
    readonly PostHogApiClient _apiClient;
    readonly LocalFeatureFlagsLoader _featureFlagsLoader;
    readonly IFeatureFlagCache _featureFlagsCache;
    readonly MemoryCache _featureFlagCalledEventCache;
    static readonly ApiResult NoOpApiResult = new(0);
    static readonly Task<ApiResult> NoOpApiResultTask = Task.FromResult(NoOpApiResult);
    static readonly IReadOnlyDictionary<string, FeatureFlag> EmptyFeatureFlags = new Dictionary<string, FeatureFlag>(0);

    readonly TimeProvider _timeProvider;
    readonly IOptions<PostHogOptions> _options;
    readonly ITaskScheduler _taskScheduler;
    readonly ILogger<PostHogClient> _logger;
    readonly bool _isDisabled;
    readonly IFeatureFlagEvaluationsHost _evaluationsHost;
    int _missingFeatureFlagDistinctIdWarningLogged;

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
        IFeatureFlagCache? featureFlagsCache = null,
        IHttpClientFactory? httpClientFactory = null,
        ITaskScheduler? taskScheduler = null,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null)
    {
        _options = NotNull(options);
        _featureFlagsCache = featureFlagsCache ?? NullFeatureFlagCache.Instance;
        httpClientFactory ??= new SimpleHttpClientFactory();
        _taskScheduler = taskScheduler ?? new TaskRunTaskScheduler();
        _timeProvider = timeProvider ?? TimeProvider.System;
        loggerFactory ??= NullLoggerFactory.Instance;
        _logger = loggerFactory.CreateLogger<PostHogClient>();

        var projectTokenMissing = NormalizeOptions(_options.Value, _logger);
        _isDisabled = _options.Value.Disabled || projectTokenMissing;

        _apiClient = new PostHogApiClient(
            httpClientFactory.CreateClient(nameof(PostHogClient)),
            options,
            _timeProvider,
            loggerFactory.CreateLogger<PostHogApiClient>()
        );
        _asyncBatchHandler = new AsyncBatchHandler<CapturedEvent, CapturedEventBatchContext>(
            batch => _apiClient.CaptureBatchAsync(batch, CancellationToken.None),
            batchContextFunc: () => new CapturedEventBatchContext(
                new FallbackFeatureFlagCache(
                    new MemoryFeatureFlagCache(_timeProvider, 10000, 0.2),
                    _featureFlagsCache)),
            options,
            _taskScheduler,
            _timeProvider,
            loggerFactory.CreateLogger<AsyncBatchHandler>());

        _featureFlagsLoader = new LocalFeatureFlagsLoader(
            _apiClient,
            options,
            _taskScheduler,
            _timeProvider,
            loggerFactory);

        _featureFlagCalledEventCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = options.Value.FeatureFlagSentCacheSizeLimit,
            Clock = new TimeProviderSystemClock(_timeProvider),
            CompactionPercentage = options.Value.FeatureFlagSentCacheCompactionPercentage
        });

        _evaluationsHost = new EvaluationsHost(this);
        _logger.LogInfoClientCreated(options.Value.MaxBatchSize, options.Value.FlushInterval, options.Value.FlushAt);
    }

    static bool NormalizeOptions(PostHogOptions options, ILogger<PostHogClient> logger)
    {
        if (options.HasLegacyProjectApiKey)
        {
            logger.LogWarningProjectApiKeyDeprecated();
        }

        options.Normalize();

        var projectTokenMissing = options.ProjectToken is null;
        if (projectTokenMissing && !options.Disabled)
        {
            logger.LogErrorProjectTokenRequired();
        }
        return projectTokenMissing;
    }

    bool CheckDisabledAndLog(string methodName)
    {
        if (!_isDisabled)
        {
            return false;
        }

        _logger.LogWarningClientDisabled(methodName);
        return true;
    }

    // A personal_api_key is only required for feature flag calls when callers explicitly request
    // local-only evaluation. Without it we cannot download local flag definitions, and the
    // local-only option means we must not fall back to remote /flags evaluation.
    bool RequiresMissingPersonalApiKey(AllFeatureFlagsOptions? options, string methodName)
        => options is { OnlyEvaluateLocally: true } && CheckPersonalApiKeyMissingAndLog(methodName);

    bool CheckPersonalApiKeyMissingAndLog(string methodName)
    {
        if (_options.Value.PersonalApiKey is not null)
        {
            return false;
        }

        _logger.LogWarningPersonalApiKeyMissing(methodName);
        return true;
    }

    void LogMissingFeatureFlagDistinctIdWarningOnce()
    {
        if (Interlocked.Exchange(ref _missingFeatureFlagDistinctIdWarningLogged, 1) == 0)
        {
            _logger.LogWarningMissingFeatureFlagDistinctId();
        }
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
    {
        if (CheckDisabledAndLog(nameof(AliasAsync)))
        {
            return NoOpApiResult;
        }

        return await _apiClient.AliasAsync(previousId, newId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ApiResult> IdentifyAsync(
        string distinctId,
        Dictionary<string, object>? personPropertiesToSet,
        Dictionary<string, object>? personPropertiesToSetOnce,
        CancellationToken cancellationToken)
    {
        if (CheckDisabledAndLog(nameof(IdentifyAsync)))
        {
            return NoOpApiResult;
        }

        return await _apiClient.IdentifyAsync(
            distinctId,
            personPropertiesToSet,
            personPropertiesToSetOnce,
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ApiResult> GroupIdentifyAsync(
        string type,
        StringOrValue<int> key,
        Dictionary<string, object>? properties,
        CancellationToken cancellationToken)
    {
        if (CheckDisabledAndLog(nameof(GroupIdentifyAsync)))
        {
            return NoOpApiResultTask;
        }

        return _apiClient.GroupIdentifyAsync(type, key, properties, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ApiResult> GroupIdentifyAsync(
        string distinctId,
        string type,
        StringOrValue<int> key,
        Dictionary<string, object>? properties,
        CancellationToken cancellationToken)
    {
        if (CheckDisabledAndLog(nameof(GroupIdentifyAsync)))
        {
            return NoOpApiResultTask;
        }

        return _apiClient.GroupIdentifyAsync(type, key, properties, cancellationToken, distinctId);
    }

    /// <inheritdoc/>
    public bool Capture(
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        FeatureFlagEvaluations? flags,
        DateTimeOffset? timestamp = null)
        => CaptureCore(distinctId, eventName, properties, groups, sendFeatureFlags: false, flags: flags, timestamp: timestamp);

    /// <inheritdoc/>
    [Obsolete("Prefer Capture(..., flags: snapshot, ...) using a FeatureFlagEvaluations snapshot from EvaluateFlagsAsync — same payload, no extra /flags request. This overload will be removed in a future major version.", error: false)]
    public bool Capture(
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        bool sendFeatureFlags,
        DateTimeOffset? timestamp = null)
        => CaptureCore(distinctId, eventName, properties, groups, sendFeatureFlags, flags: null, timestamp: timestamp);

    bool CaptureCore(
        string? distinctId,
        string eventName,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        bool sendFeatureFlags,
        FeatureFlagEvaluations? flags,
        DateTimeOffset? timestamp)
    {
        if (CheckDisabledAndLog(nameof(Capture)))
        {
            return false;
        }

        // If custom timestamp provided, add it to properties
        if (timestamp.HasValue)
        {
            properties = AddTimestampToProperties(properties, timestamp.Value);
        }

        var captureContext = PostHogContextHelper.ResolveCaptureContext(distinctId, properties);

        var capturedEvent = new CapturedEvent(
            eventName,
            captureContext.DistinctId,
            captureContext.Properties,
            timestamp: timestamp ?? _timeProvider.GetUtcNow());

        if (groups is { Count: > 0 })
        {
            capturedEvent.Properties["$groups"] = groups.ToDictionary(g => g.GroupType, g => g.GroupKey);
        }

        capturedEvent.Properties.Merge(_options.Value.SuperProperties);

        var batchItem = new BatchItem<CapturedEvent, CapturedEventBatchContext>(BatchTask);

        if (_asyncBatchHandler.Enqueue(batchItem))
        {
            _logger.LogTraceCaptureCalled(eventName, capturedEvent.Properties.Count, _asyncBatchHandler.Count);
            return true;
        }
        _logger.LogWarnCaptureFailed(eventName, capturedEvent.Properties.Count, _asyncBatchHandler.Count);
        return false;

        Task<CapturedEvent> BatchTask(CapturedEventBatchContext context)
        {
            if (flags is not null)
            {
                AddFeatureFlagsToCapturedEvent(capturedEvent, flags);
                return Task.FromResult(capturedEvent);
            }

            if (!sendFeatureFlags)
            {
                return Task.FromResult(capturedEvent);
            }

            // Prefer local evaluation when available
            if (_featureFlagsLoader.IsLoaded)
            {
                return AddLocalFeatureFlagDataAsync(captureContext.DistinctId, groups, capturedEvent);
            }

            // Otherwise we fall back to remote /flags call
            return AddFreshFeatureFlagDataAsync(context.FeatureFlagCache, captureContext.DistinctId, groups, capturedEvent);
        }
    }

    /// <inheritdoc/>
    [Obsolete("Prefer CaptureException(..., flags: snapshot, ...) using a FeatureFlagEvaluations snapshot from EvaluateFlagsAsync — same payload, no extra /flags request. This overload will be removed in a future major version.", error: false)]
    public bool CaptureException(
        Exception exception,
        string distinctId,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        bool sendFeatureFlags,
        DateTimeOffset? timestamp = null)
        => CaptureExceptionCore(exception, distinctId, properties, groups, sendFeatureFlags, flags: null, timestamp: timestamp);

    /// <inheritdoc/>
    public bool CaptureException(
        Exception exception,
        string distinctId,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        FeatureFlagEvaluations? flags,
        DateTimeOffset? timestamp = null)
        => CaptureExceptionCore(exception, distinctId, properties, groups, sendFeatureFlags: false, flags: flags, timestamp: timestamp);

    bool CaptureExceptionCore(
        Exception exception,
        string? distinctId,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        bool sendFeatureFlags,
        FeatureFlagEvaluations? flags,
        DateTimeOffset? timestamp)
    {
        if (CheckDisabledAndLog(nameof(CaptureException)))
        {
            return false;
        }

        if (exception == null)
        {
            _logger.LogErrorCaptureExceptionNull();
            return false;
        }

        // Should never throw exceptions in this method to avoid re-raising exceptions to user code, log instead
        try
        {
            var host = _options.Value.HostUrl.ToString().TrimEnd('/').Replace(".i.", ".", StringComparison.Ordinal);
            properties ??= [];
            var identity = PostHogContextHelper.ResolveIdentity(distinctId, PostHogContext.Current);
            if (identity.IsPersonless && !properties.ContainsKey(PostHogProperties.ProcessPersonProfile))
            {
                properties[PostHogProperties.ProcessPersonProfile] = false;
            }

            properties["$exception_personURL"] = $"{host}/project/{_options.Value.ProjectToken}/person/{identity.DistinctId}";
            properties = ExceptionPropertiesBuilder.Build(properties, exception);

            return CaptureCore(identity.DistinctId, "$exception", properties, groups, sendFeatureFlags, flags, timestamp);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception e)
#pragma warning restore CA1031
        {
            _logger.LogErrorCaptureExceptionFailed(e);
            return false;
        }
    }



    async Task<CapturedEvent> AddFreshFeatureFlagDataAsync(
        IFeatureFlagCache featureFlagCache,
        string distinctId,
        GroupCollection? groups,
        CapturedEvent capturedEvent)
    {
        var result = await featureFlagCache.GetAndCacheFlagsAsync(
            distinctId,
            personProperties: null,
            groups: groups,
            (userId, ctx) => FetchFlagsAsync(
                userId,
                options: new AllFeatureFlagsOptions
                {
                    Groups = groups
                },
                ctx),
            CancellationToken.None);

        return AddFeatureFlagsToCapturedEvent(capturedEvent, result.Flags);
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

    static CapturedEvent AddFeatureFlagsToCapturedEvent(
        CapturedEvent capturedEvent,
        FeatureFlagEvaluations flags)
    {
        // Single-pass: per-flag $feature/<key> property + $active_feature_flags collection in one
        // enumeration of the records dictionary. Runs per captured event, so worth keeping tight.
        var active = new List<string>(flags.Records.Count);
        foreach (var (key, record) in flags.Records)
        {
            capturedEvent.Properties[$"$feature/{key}"] = record.Flag.ToResponseObject();
            if (record.Enabled)
            {
                active.Add(key);
            }
        }
        capturedEvent.Properties["$active_feature_flags"] = active.ToArray();
        return capturedEvent;
    }

    /// <inheritdoc/>
    [Obsolete("Prefer EvaluateFlagsAsync(distinctId).IsEnabled(featureKey) — one /flags request powers all flag branching for the request. This method will be removed in a future major version.", error: false)]
    public async Task<bool> IsFeatureEnabledAsync(
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options,
        CancellationToken cancellationToken)
    {
        if (CheckDisabledAndLog(nameof(IsFeatureEnabledAsync)))
        {
            return false;
        }

        if (RequiresMissingPersonalApiKey(options, nameof(IsFeatureEnabledAsync)))
        {
            return false;
        }

#pragma warning disable CS0618 // Internal call into the deprecated path; see method docstring for the preferred API.
        var result = await GetFeatureFlagAsync(
            featureKey,
            distinctId,
            options,
            cancellationToken);
#pragma warning restore CS0618

        return result is { IsEnabled: true };
    }

    /// <inheritdoc/>
    [Obsolete("Prefer EvaluateFlagsAsync(distinctId).GetFlag(featureKey) — one /flags request powers all flag branching for the request. This method will be removed in a future major version.", error: false)]
    public async Task<FeatureFlag?> GetFeatureFlagAsync(
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options,
        CancellationToken cancellationToken)
    {
        if (CheckDisabledAndLog(nameof(GetFeatureFlagAsync)))
        {
            return null;
        }

        if (RequiresMissingPersonalApiKey(options, nameof(GetFeatureFlagAsync)))
        {
            return null;
        }

        var resolvedDistinctId = PostHogContextHelper.ResolveDistinctId(distinctId);
        if (resolvedDistinctId is null)
        {
            LogMissingFeatureFlagDistinctIdWarningOnce();
            return null;
        }

        LocalEvaluator? localEvaluator;
        try
        {
            localEvaluator = await _featureFlagsLoader.GetFeatureFlagsForLocalEvaluationAsync(cancellationToken);
        }
        catch (ApiException e) when (e.ErrorType is "quota_limited")
        {
            _logger.LogWarningQuotaExceeded(e);
            return null;
        }

        FeatureFlag? response = null;
        if (localEvaluator is not null && localEvaluator.TryGetLocalFeatureFlag(featureKey, out var localFeatureFlag))
        {
            try
            {
                var value = localEvaluator.ComputeFlagLocally(
                    localFeatureFlag,
                    resolvedDistinctId,
                    options?.Groups ?? [],
                    options?.PersonProperties ?? []);
                response = FeatureFlag.CreateFromLocalEvaluation(featureKey, value, localFeatureFlag);
                _logger.LogDebugSuccessLocally(featureKey, response);
            }
            catch (RequiresServerEvaluationException e)
            {
                _logger.LogDebugFailedToComputeFlag(e, featureKey);
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
        string? requestId = null;
        long? evaluatedAt = null;
        var errors = new List<string>();
        FlagsResult? flagsResult = null;

        void HandleRemoteError(Exception ex, string errorType)
        {
            _logger.LogErrorUnableToGetRemotely(ex, featureKey);
            errors.Add(errorType);
            response = new FeatureFlag { Key = featureKey, IsEnabled = false };
        }

        if (!flagWasLocallyEvaluated && options is not { OnlyEvaluateLocally: true })
        {
            try
            {
                // Fallback to remote evaluation via the /flags endpoint.
                flagsResult = await FetchFlagsAsync(
                    resolvedDistinctId,
                    options ?? new FeatureFlagOptions
                    {
                        FlagKeysToEvaluate = [featureKey]
                    },
                    cancellationToken);
                requestId = flagsResult.RequestId;
                evaluatedAt = flagsResult.EvaluatedAt;

                if (flagsResult.ErrorsWhileComputingFlags)
                {
                    errors.Add(FeatureFlagError.ErrorsWhileComputingFlags);
                }

                if (flagsResult.QuotaLimited.Contains("feature_flags"))
                {
                    errors.Add(FeatureFlagError.QuotaLimited);
                }

                response = flagsResult.Flags.GetValueOrDefault(featureKey);
                if (response is null)
                {
                    errors.Add(FeatureFlagError.FlagMissing);
                    response = new FeatureFlag
                    {
                        Key = featureKey,
                        IsEnabled = false
                    };
                }

                _logger.LogDebugSuccessRemotely(featureKey, response);
            }
            catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                HandleRemoteError(e, FeatureFlagError.Timeout);
            }
            catch (HttpRequestException e)
            {
                HandleRemoteError(e, FeatureFlagError.ConnectionError);
            }
            catch (ApiException e)
            {
                HandleRemoteError(e, FeatureFlagError.ApiError((int)e.Status));
            }
            catch (Exception e) when (e is not ArgumentException and not NullReferenceException and not OperationCanceledException)
            {
                HandleRemoteError(e, FeatureFlagError.UnknownError);
            }
        }

        options ??= new FeatureFlagOptions(); // We need the defaults if options is null.

        if (options.SendFeatureFlagEvents)
        {
            var properties = BuildFeatureFlagCalledProperties(
                featureKey,
                response,
                requestId,
                evaluatedAt,
                errors,
                locallyEvaluated: flagWasLocallyEvaluated,
                flagDefinitionsLoadedAt: flagWasLocallyEvaluated
                    ? _featureFlagsLoader.FlagDefinitionsLoadedAt
                    : null);

            TryCaptureDedupedFeatureFlagCalledEvent(
                resolvedDistinctId,
                featureKey,
                cacheKeyValue: (string)response,
                properties,
                options.Groups,
                cancellationToken);
        }

        return response;
    }

    /// <inheritdoc/>
    public async Task<JsonDocument?> GetRemoteConfigPayloadAsync(string key, CancellationToken cancellationToken)
    {
        if (CheckDisabledAndLog(nameof(GetRemoteConfigPayloadAsync)))
        {
            return null;
        }

        if (CheckPersonalApiKeyMissingAndLog(nameof(GetRemoteConfigPayloadAsync)))
        {
            return null;
        }

        try
        {
            var document = await _apiClient.GetRemoteConfigPayloadAsync(key, cancellationToken);

            // The remote config endpoint returns JSON encoded in a string.
            // For example: "{\"foo\": \"bar\",\"baz\": 42}"
            // Instead of:  {"foo": "bar","baz": 42}
            // However, we may change that in the future.
            // So this is implemented in a forward-compatible way.
            if (document is { RootElement.ValueKind: JsonValueKind.String } doc
                && doc.RootElement.GetString() is { } innerJson
                && TryParseJson(innerJson, out var parsedJson))
            {
                return parsedJson;
            }

            return document;
        }
        catch (Exception e) when (e is not ArgumentException and not NullReferenceException)
        {
            _logger.LogErrorUnableToGetRemoteConfigPayload(e);
            return null;
        }

        static bool TryParseJson(string json, out JsonDocument? document)
        {
            try
            {
                document = JsonDocument.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                document = null;
                return false;
            }
        }
    }

    static Dictionary<string, object> BuildFeatureFlagCalledProperties(
        string featureKey,
        FeatureFlag? flag,
        string? requestId,
        long? evaluatedAt,
        List<string> errors,
        bool locallyEvaluated,
        long? flagDefinitionsLoadedAt)
    {
        var properties = new Dictionary<string, object>
        {
            ["$feature_flag"] = featureKey,
            ["$feature_flag_response"] = flag.ToResponseObject(),
            ["locally_evaluated"] = locallyEvaluated,
            [$"$feature/{featureKey}"] = flag.ToResponseObject()
        };
        if (locallyEvaluated)
        {
            properties["$feature_flag_reason"] = "Evaluated locally";
            if (flagDefinitionsLoadedAt is not null)
            {
                properties["$feature_flag_definitions_loaded_at"] = flagDefinitionsLoadedAt;
            }
        }
        else if (flag is FeatureFlagWithMetadata featureFlag)
        {
            properties["$feature_flag_id"] = featureFlag.Id;
            properties["$feature_flag_version"] = featureFlag.Version;
            properties["$feature_flag_reason"] = featureFlag.Reason;
        }

        if (requestId is not null)
        {
            properties["$feature_flag_request_id"] = requestId;
        }

        if (evaluatedAt is not null)
        {
            properties["$feature_flag_evaluated_at"] = evaluatedAt;
        }

        if (errors.Count > 0)
        {
            properties["$feature_flag_error"] = string.Join(",", errors);
        }

        return properties;
    }

    void TryCaptureDedupedFeatureFlagCalledEvent(
        string distinctId,
        string featureKey,
        string cacheKeyValue,
        Dictionary<string, object> properties,
        GroupCollection? groups,
        CancellationToken cancellationToken)
    {
        _featureFlagCalledEventCache.GetOrCreate(
            key: (distinctId, featureKey, cacheKeyValue),
            // This factory only runs when the (distinct id, key, value) tuple is not yet cached.
            factory: cacheEntry =>
            {
                cacheEntry.SetSize(1);
                cacheEntry.SetPriority(CacheItemPriority.Low);
                cacheEntry.SetSlidingExpiration(_options.Value.FeatureFlagSentCacheSlidingExpiration);

                CaptureCore(
                    distinctId,
                    eventName: "$feature_flag_called",
                    properties: properties,
                    groups: groups,
                    sendFeatureFlags: false,
                    flags: null,
                    timestamp: null);
                return true;
            });

        if (_featureFlagCalledEventCache.Count >= _options.Value.FeatureFlagSentCacheSizeLimit)
        {
            // Fire-and-forget the compaction because it can be expensive.
            _taskScheduler.Run(
                () => _featureFlagCalledEventCache.Compact(
                    _options.Value.FeatureFlagSentCacheCompactionPercentage),
                cancellationToken);
        }
    }

    sealed class EvaluationsHost : IFeatureFlagEvaluationsHost
    {
        readonly PostHogClient _client;

        public EvaluationsHost(PostHogClient client) => _client = client;

        public void CaptureFeatureFlagCalled(
            string distinctId,
            string featureKey,
            EvaluatedFlagRecord? record,
            GroupCollection? groups,
            string? requestId,
            long? evaluatedAt,
            long? flagDefinitionsLoadedAt,
            IReadOnlyCollection<string> errors)
        {
            // Mirror the legacy path's "missing flag" handling: append the FlagMissing error
            // and use a synthetic disabled FeatureFlag so the response shape is consistent.
            var snapshotErrors = new List<string>(errors);
            if (record is null)
            {
                snapshotErrors.Add(FeatureFlagError.FlagMissing);
            }

            var flag = record?.Flag ?? new FeatureFlag { Key = featureKey, IsEnabled = false };
            var cacheKeyValue = record?.CacheKeyValue ?? (string)flag;

            var properties = BuildFeatureFlagCalledProperties(
                featureKey,
                flag,
                requestId,
                evaluatedAt,
                snapshotErrors,
                locallyEvaluated: record?.LocallyEvaluated ?? false,
                flagDefinitionsLoadedAt: record?.LocallyEvaluated == true ? flagDefinitionsLoadedAt : null);

            _client.TryCaptureDedupedFeatureFlagCalledEvent(
                distinctId,
                featureKey,
                cacheKeyValue,
                properties,
                groups,
                CancellationToken.None);
        }

        public void LogFilterWarning(string message)
        {
            if (!_client._options.Value.FeatureFlagsLogWarnings)
            {
                return;
            }
            _client._logger.LogWarningFeatureFlagFilter(message);
        }
    }

    /// <inheritdoc/>
    public async Task<FeatureFlagEvaluations> EvaluateFlagsAsync(
        string distinctId,
        AllFeatureFlagsOptions? options,
        CancellationToken cancellationToken)
    {
        var resolvedDistinctId = PostHogContextHelper.ResolveDistinctId(distinctId) ?? string.Empty;
        if (CheckDisabledAndLog(nameof(EvaluateFlagsAsync)))
        {
            return FeatureFlagEvaluations.Empty(_evaluationsHost, resolvedDistinctId);
        }

        if (RequiresMissingPersonalApiKey(options, nameof(EvaluateFlagsAsync)))
        {
            return FeatureFlagEvaluations.Empty(_evaluationsHost, resolvedDistinctId);
        }

        if (resolvedDistinctId.Length == 0)
        {
            // Empty distinct id is a safety fallback. Returning an empty snapshot avoids leaking
            // events with empty distinct ids when the caller forgot to resolve one.
            LogMissingFeatureFlagDistinctIdWarningOnce();
            return FeatureFlagEvaluations.Empty(_evaluationsHost, string.Empty);
        }

        var records = new Dictionary<string, EvaluatedFlagRecord>(StringComparer.Ordinal);
        var errors = new List<string>();
        string? requestId = null;
        long? evaluatedAt = null;
        long? flagDefinitionsLoadedAt = null;

        // 1. Local pass.
        var fallbackToRemote = true;
        if (_options.Value.PersonalApiKey is not null)
        {
            try
            {
                var localEvaluator =
                    await _featureFlagsLoader.GetFeatureFlagsForLocalEvaluationAsync(cancellationToken);
                if (localEvaluator is not null)
                {
                    var (locallyEvaluated, needsRemote) = localEvaluator.EvaluateAllFlags(
                        resolvedDistinctId,
                        options?.Groups,
                        options?.PersonProperties,
                        warnOnUnknownGroups: false);

                    foreach (var (key, flag) in locallyEvaluated)
                    {
                        records[key] = ToRecord(key, flag, locallyEvaluated: true);
                    }

                    if (locallyEvaluated.Count > 0)
                    {
                        flagDefinitionsLoadedAt = _featureFlagsLoader.FlagDefinitionsLoadedAt;
                    }

                    fallbackToRemote = needsRemote && options is not { OnlyEvaluateLocally: true };
                }
            }
            catch (ApiException e) when (e.ErrorType is "quota_limited")
            {
                // Quota-limited from the local-evaluation endpoint. In practice this fires from
                // GetFeatureFlagsForLocalEvaluationAsync — the first call in the try, before any
                // flag has been evaluated — so `records` is empty here. We surface the error on
                // $feature_flag_called and skip the remote pass to mirror the remote-pass behavior.
                _logger.LogWarningQuotaExceeded(e);
                errors.Add(FeatureFlagError.QuotaLimited);
                fallbackToRemote = false;
            }
        }

        // 2. Remote pass — only if we still need it.
        if (fallbackToRemote && options is not { OnlyEvaluateLocally: true })
        {
            try
            {
                var flagsResult = await FetchFlagsAsync(resolvedDistinctId, options, cancellationToken);
                requestId = flagsResult.RequestId;
                evaluatedAt = flagsResult.EvaluatedAt;

                if (flagsResult.ErrorsWhileComputingFlags)
                {
                    errors.Add(FeatureFlagError.ErrorsWhileComputingFlags);
                }

                if (flagsResult.QuotaLimited.Contains("feature_flags"))
                {
                    errors.Add(FeatureFlagError.QuotaLimited);
                }

                foreach (var (key, flag) in flagsResult.Flags)
                {
                    // Local-wins merge: keep the locally-evaluated record (which carries
                    // locally_evaluated=true and $feature_flag_definitions_loaded_at) and only fill
                    // in keys the local pass couldn't resolve. Differs from GetAllFeatureFlagsAsync,
                    // which discards local results entirely on remote fallback.
                    if (!records.ContainsKey(key))
                    {
                        records[key] = ToRecord(key, flag, locallyEvaluated: false);
                    }
                }
            }
            catch (Exception e) when (e is not ArgumentException
                                       and not NullReferenceException
                                       and not OperationCanceledException)
            {
                _logger.LogErrorUnableToGetFeatureFlagsAndPayloads(e);
                errors.Add(FeatureFlagError.UnknownError);
            }
        }

        return new FeatureFlagEvaluations(
            _evaluationsHost,
            resolvedDistinctId,
            records,
            requestId,
            evaluatedAt,
            flagDefinitionsLoadedAt,
            options?.Groups,
            errors);

        static EvaluatedFlagRecord ToRecord(string key, FeatureFlag flag, bool locallyEvaluated)
            => new()
            {
                Key = key,
                Flag = flag,
                Enabled = flag.IsEnabled,
                CacheKeyValue = (string)flag,
                LocallyEvaluated = locallyEvaluated,
            };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, FeatureFlag>> GetAllFeatureFlagsAsync(
        string distinctId,
        AllFeatureFlagsOptions? options,
        CancellationToken cancellationToken)
    {
        if (CheckDisabledAndLog(nameof(GetAllFeatureFlagsAsync)))
        {
            return EmptyFeatureFlags;
        }

        if (RequiresMissingPersonalApiKey(options, nameof(GetAllFeatureFlagsAsync)))
        {
            return EmptyFeatureFlags;
        }

        var resolvedDistinctId = PostHogContextHelper.ResolveDistinctId(distinctId);
        if (resolvedDistinctId is null)
        {
            LogMissingFeatureFlagDistinctIdWarningOnce();
            return EmptyFeatureFlags;
        }

        if (_options.Value.PersonalApiKey is not null)
        {
            // Attempt to load local feature flags.
            try
            {
                var localEvaluator =
                    await _featureFlagsLoader.GetFeatureFlagsForLocalEvaluationAsync(cancellationToken);
                if (localEvaluator is not null)
                {
                    var (localEvaluationResults, fallbackToRemote) = localEvaluator.EvaluateAllFlags(
                        resolvedDistinctId,
                        options?.Groups,
                        options?.PersonProperties,
                        warnOnUnknownGroups: false);

                    if (!fallbackToRemote || options is { OnlyEvaluateLocally: true })
                    {
                        return localEvaluationResults;
                    }
                }
            }
            catch (ApiException e) when (e.ErrorType is "quota_limited")
            {
                _logger.LogWarningQuotaExceeded(e);
                return EmptyFeatureFlags;
            }
        }

        try
        {
            var flagsResult = await FetchFlagsAsync(resolvedDistinctId, options, cancellationToken);
            return flagsResult.Flags;
        }
        catch (Exception e) when (e is not ArgumentException and not NullReferenceException)
        {
            _logger.LogErrorUnableToGetFeatureFlagsAndPayloads(e);
            return EmptyFeatureFlags;
        }
    }

    // Retrieves all the evaluated feature flags from the /flags endpoint.
    async Task<FlagsResult> FetchFlagsAsync(
        string distinctId,
        AllFeatureFlagsOptions? options,
        CancellationToken cancellationToken) =>
        await FetchFlagsAsync(_featureFlagsCache, distinctId, options, cancellationToken);

    async Task<FlagsResult> FetchFlagsAsync(
        IFeatureFlagCache cache,
        string distinctId,
        AllFeatureFlagsOptions? options,
        CancellationToken cancellationToken)
    {
        var result = await cache.GetAndCacheFlagsAsync(
            distinctId,
            personProperties: options?.PersonProperties,
            groups: options?.Groups,
            fetcher: FetchFlagsAsync,
            cancellationToken: cancellationToken);

        if (result.QuotaLimited.Contains("feature_flags"))
        {
            _logger.LogWarningQuotaExceeded();
            return new FlagsResult { QuotaLimited = result.QuotaLimited };
        }

        return result;

        async Task<FlagsResult> FetchFlagsAsync(string distId, CancellationToken ctx)
        {
            var results = await _apiClient.GetFeatureFlagsAsync(
                distId,
                options?.PersonProperties,
                options?.Groups,
                options?.FlagKeysToEvaluate,
                ctx);
            return results.ToFlagsResult();
        }
    }

    /// <inheritdoc/>
    public async Task LoadFeatureFlagsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInfoLoadFeatureFlags();

        if (CheckDisabledAndLog(nameof(LoadFeatureFlagsAsync)))
        {
            return;
        }

        if (CheckPersonalApiKeyMissingAndLog(nameof(LoadFeatureFlagsAsync)))
        {
            return;
        }

        try
        {
            // Refresh feature flags (ETag will be used for conditional requests to minimize bandwidth)
            await _featureFlagsLoader.RefreshAsync(cancellationToken);

            // Determine polling status for logging
            var pollingStatus = _featureFlagsLoader.IsLoaded ? "active" : "inactive";
            _logger.LogDebugFeatureFlagsLoaded(pollingStatus);
        }
        catch (ApiException e) when (e.ErrorType is "quota_limited")
        {
            _logger.LogWarningQuotaExceeded(e);
            throw;
        }
        catch (Exception e) when (e is not ArgumentException and not NullReferenceException and not OperationCanceledException)
        {
            _logger.LogErrorFailedToLoadFeatureFlags(e);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task FlushAsync()
    {
        if (CheckDisabledAndLog(nameof(FlushAsync)))
        {
            return;
        }

        await _asyncBatchHandler.FlushAsync();
    }

    /// <inheritdoc/>
    public string Version => VersionConstants.Version;

    // HACK: Temporary hack until we come up with a better approach. This is to support Feature Management
    //       in the PostHog.AspNetCore package, which is why I don't want to make it public here.
    [Obsolete("This method is for internal use only and may go away soon.")]
    internal async Task<LocalEvaluator?> GetLocalEvaluatorAsync(CancellationToken cancellationToken)
    {
        if (CheckDisabledAndLog(nameof(GetLocalEvaluatorAsync)))
        {
            return null;
        }

        try
        {
            return await _featureFlagsLoader.GetFeatureFlagsForLocalEvaluationAsync(cancellationToken);
        }
        catch (ApiException e) when (e.ErrorType is "quota_limited")
        {
            _logger.LogWarningQuotaExceeded(e);
            return null;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// Clears the local flags cache.
    /// </summary>
    public void ClearLocalFlagsCache()
    {
        if (CheckDisabledAndLog(nameof(ClearLocalFlagsCache)))
        {
            return;
        }

        _featureFlagsLoader.Clear();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Stop background tasks first, while the API client is still alive.
        // The polling task in _featureFlagsLoader may call the API client during shutdown.
        try
        {
            await _asyncBatchHandler.DisposeAsync();
            await _featureFlagsLoader.DisposeAsync();
        }
        finally
        {
            _apiClient.Dispose();
            _featureFlagCalledEventCache.Dispose();
        }
    }



    static Dictionary<string, object>? AddTimestampToProperties(Dictionary<string, object>? properties, DateTimeOffset timestamp)
    {
        properties ??= new Dictionary<string, object>();
        properties["timestamp"] = timestamp;
        return properties;
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
        Level = LogLevel.Error,
        Message = "[FEATURE FLAGS] Error while fetching remote config payload.")]
    public static partial void LogErrorUnableToGetRemoteConfigPayload(
        this ILogger<PostHogClient> logger,
        Exception exception);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Error,
        Message = "[FEATURE FLAGS] Unable to get feature flags and payloads")]
    public static partial void LogErrorUnableToGetFeatureFlagsAndPayloads(this ILogger<PostHogClient> logger, Exception exception);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Warning,
        Message = "[FEATURE FLAGS] Quota exceeded, resetting feature flag data. Learn more about billing limits at https://posthog.com/docs/billing/limits-alerts")]
    public static partial void LogWarningQuotaExceeded(this ILogger<PostHogClient> logger);

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Warning,
        Message = "[FEATURE FLAGS] Quota exceeded, resetting feature flag data. Learn more about billing limits at https://posthog.com/docs/billing/limits-alerts")]
    public static partial void LogWarningQuotaExceeded(this ILogger<PostHogClient> logger, Exception e);

    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Warning,
        Message = "ProjectApiKey is deprecated and will be removed in the next major version. Use ProjectToken instead.")]
    public static partial void LogWarningProjectApiKeyDeprecated(this ILogger<PostHogClient> logger);

    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Error,
        Message = "Either ProjectToken or ProjectApiKey must be provided.")]
    public static partial void LogErrorProjectTokenRequired(this ILogger<PostHogClient> logger);

    [LoggerMessage(
        EventId = 15,
        Level = LogLevel.Information,
        Message = "[FEATURE FLAGS] Loading feature flags for local evaluation")]
    public static partial void LogInfoLoadFeatureFlags(this ILogger<PostHogClient> logger);

    [LoggerMessage(
        EventId = 16,
        Level = LogLevel.Debug,
        Message = "[FEATURE FLAGS] Feature flags loaded successfully, polling {PollingStatus}")]
    public static partial void LogDebugFeatureFlagsLoaded(this ILogger<PostHogClient> logger, string pollingStatus);

    [LoggerMessage(
        EventId = 17,
        Level = LogLevel.Error,
        Message = "[FEATURE FLAGS] Failed to load feature flags")]
    public static partial void LogErrorFailedToLoadFeatureFlags(this ILogger<PostHogClient> logger, Exception exception);

    [LoggerMessage(
        EventId = 18,
        Level = LogLevel.Error,
        Message = "CaptureException called with null exception")]
    public static partial void LogErrorCaptureExceptionNull(this ILogger<PostHogClient> logger);

    [LoggerMessage(
        EventId = 19,
        Level = LogLevel.Error,
        Message = "CaptureException failed with an exception")]
    public static partial void LogErrorCaptureExceptionFailed(this ILogger<PostHogClient> logger, Exception exception);

    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Warning,
        Message = "PostHog SDK is disabled; {MethodName} is a no-op.")]
    public static partial void LogWarningClientDisabled(this ILogger<PostHogClient> logger, string methodName);

    [LoggerMessage(
        EventId = 21,
        Level = LogLevel.Warning,
        Message = "PostHog personal_api_key is not configured; {MethodName} is a no-op.")]
    public static partial void LogWarningPersonalApiKeyMissing(this ILogger<PostHogClient> logger, string methodName);

    [LoggerMessage(
        EventId = 22,
        Level = LogLevel.Warning,
        Message = "[FEATURE FLAGS] {Message}")]
    public static partial void LogWarningFeatureFlagFilter(this ILogger<PostHogClient> logger, string message);

    [LoggerMessage(
        EventId = 23,
        Level = LogLevel.Warning,
        Message = "[FEATURE FLAGS] distinctId is required to evaluate feature flags. Pass a distinctId explicitly or use PostHog request context for the current request.")]
    public static partial void LogWarningMissingFeatureFlagDistinctId(this ILogger<PostHogClient> logger);
}
