using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog.Api;
using PostHog.Library;
#if NETSTANDARD2_0 || NETSTANDARD2_1
#endif

namespace PostHog.Features;

/// <summary>
/// This class is responsible for loading the feature flags from the PostHog API and storing them locally.
/// It polls the API at a regular interval (set in <see cref="PostHogOptions"/>) and stores the result in memory.
/// </summary>
/// <param name="postHogApiClient">The <see cref="PostHogApiClient"/> used to make requests.</param>
/// <param name="options">The options used to configure the client.</param>
/// <param name="timeProvider">The time provider <see cref="TimeProvider"/> to use to determine time.</param>
/// <param name="taskScheduler">Used to run tasks on the background.</param>
/// <param name="loggerFactory">Creates loggers used by the loader and local evaluator.</param>
internal sealed class LocalFeatureFlagsLoader(
    PostHogApiClient postHogApiClient,
    IOptions<PostHogOptions> options,
    ITaskScheduler taskScheduler,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory) : IDisposable, IAsyncDisposable
{
    volatile int _started;
    volatile int _disposed;
    volatile Task? _pollingTask;
    LocalEvaluator? _localEvaluator;
    long _flagDefinitionsLoadedAtMs; // Unix milliseconds; 0 means not yet loaded.
    volatile string? _etag; // ETag for conditional requests to reduce bandwidth
    readonly CancellationTokenSource _cancellationTokenSource = new();
    readonly PeriodicTimer _timer = new(options.Value.FeatureFlagPollInterval, timeProvider);
    readonly ILogger<LocalFeatureFlagsLoader> _logger = loggerFactory.CreateLogger<LocalFeatureFlagsLoader>();
    readonly ILogger<LocalEvaluator> _localEvaluatorLogger = loggerFactory.CreateLogger<LocalEvaluator>();
    readonly object _warnedUnresolvableFlagsLock = new();
    // The definition and reason of each flag already warned about as never locally resolvable.
    Dictionary<string, (LocalFeatureFlag Definition, UnresolvedFlagReason Reason)> _warnedUnresolvableFlags = new(StringComparer.Ordinal);

    void StartPollingIfNotStarted()
    {
        // If we've started polling, don't start another poll.
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 1)
        {
            return;
        }
        _pollingTask = taskScheduler.Run(() => PollForFeatureFlagsAsync(_cancellationTokenSource.Token));
    }

    /// <summary>
    /// Retrieves the feature flags from the local cache. If the cache is empty, it will fetch the flags from the API.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>All the feature flags.</returns>
    /// <exception cref="ApiException">Thrown when the API returns a <c>quota_limited</c> error.</exception>
    public async ValueTask<LocalEvaluator?> GetFeatureFlagsForLocalEvaluationAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.SecretKey))
        {
            // Local evaluation is not enabled since it requires a secret key.
            return null;
        }
        if (_localEvaluator is { } localEvaluator)
        {
            return localEvaluator;
        }
        return await LoadLocalEvaluatorAsync(cancellationToken);
    }

    /// <summary>
    /// Forces a refresh of feature flags from the API. Uses ETag for conditional requests
    /// to minimize bandwidth when flags haven't changed (304 Not Modified).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The local evaluator with the feature flags.</returns>
    public async ValueTask<LocalEvaluator?> RefreshAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.SecretKey))
        {
            return null;
        }

        try
        {
            return await LoadLocalEvaluatorAsync(cancellationToken);
        }
        catch (ApiException e) when (e.ErrorType is "quota_limited")
        {
            Interlocked.Exchange(ref _etag, null); // Clear ETag on quota limit so next request starts fresh
            throw;
        }
    }

    async Task<LocalEvaluator?> LoadLocalEvaluatorAsync(CancellationToken cancellationToken)
    {
        StartPollingIfNotStarted();
        var response = await postHogApiClient.GetFeatureFlagsForLocalEvaluationAsync(_etag, cancellationToken);

        // If 304 Not Modified, keep using cached data (update ETag if server sent a new one)
        if (response.IsNotModified)
        {
            if (response.ETag is not null)
            {
                Interlocked.Exchange(ref _etag, response.ETag);
            }
            return _localEvaluator;
        }

        // On failure (no result), preserve existing ETag for retry
        if (response.Result is null)
        {
            return _localEvaluator;
        }

        // Success: update ETag (or clear if server stopped sending one)
        Interlocked.Exchange(ref _etag, response.ETag);

        var localEvaluator = new LocalEvaluator(response.Result, timeProvider, _localEvaluatorLogger);
        Interlocked.Exchange(ref _localEvaluator, localEvaluator);
        Interlocked.Exchange(ref _flagDefinitionsLoadedAtMs, timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        try
        {
            WarnAboutUnresolvableFlags(localEvaluator);
        }
#pragma warning disable CA1031 // The warning is diagnostic only. A definition the scan cannot read must not stop polling or fail a flag call.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _logger.LogErrorUnexpectedException(e);
        }
        return localEvaluator;
    }

    // Warns once about each flag whose definition local evaluation can never resolve, until its definition changes.
    void WarnAboutUnresolvableFlags(LocalEvaluator localEvaluator)
    {
        List<string>? experienceContinuityFlags = null;
        List<string>? unsupportedDefinitionFlags = null;
        lock (_warnedUnresolvableFlagsLock)
        {
            // Rebuilt from the current definitions, so a flag that is deleted or becomes resolvable is forgotten
            // and warns again if it later returns with an unresolvable definition.
            var warnedFlags = new Dictionary<string, (LocalFeatureFlag Definition, UnresolvedFlagReason Reason)>(StringComparer.Ordinal);
            foreach (var flag in localEvaluator.LocalEvaluationApiResult.Flags)
            {
                if (localEvaluator.GetDefinitionUnresolvedReason(flag) is not { } reason)
                {
                    continue;
                }

                warnedFlags[flag.Key] = (flag, reason);
                if (_warnedUnresolvableFlags.TryGetValue(flag.Key, out var warned)
                    && warned.Reason == reason
                    && warned.Definition.Equals(flag))
                {
                    continue;
                }

                if (reason is UnresolvedFlagReason.ExperienceContinuity)
                {
                    (experienceContinuityFlags ??= []).Add(flag.Key);
                }
                else
                {
                    (unsupportedDefinitionFlags ??= []).Add(flag.Key);
                }
            }
            _warnedUnresolvableFlags = warnedFlags;
        }

        if (experienceContinuityFlags is not null)
        {
            _logger.LogWarningExperienceContinuityFlags(experienceContinuityFlags.Count, string.Join(", ", experienceContinuityFlags));
        }

        if (unsupportedDefinitionFlags is not null)
        {
            _logger.LogWarningUnsupportedDefinitionFlags(unsupportedDefinitionFlags.Count, string.Join(", ", unsupportedDefinitionFlags));
        }
    }

    async Task PollForFeatureFlagsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await LoadLocalEvaluatorAsync(cancellationToken);
                }
                catch (ApiException e) when (e.ErrorType is "quota_limited")
                {
                    Interlocked.Exchange(ref _etag, null); // Clear ETag on quota limit
                    _logger.LogWarningQuotaExceeded(e);
                    return;
                }
                catch (Exception e) when (e is not ArgumentException and not NullReferenceException and not OperationCanceledException)
                {
                    _logger.LogErrorUnexpectedException(e);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogTraceOperationCancelled(nameof(PollForFeatureFlagsAsync));
        }
    }

    public bool IsLoaded => _localEvaluator is not null;

    /// <summary>
    /// The Unix-millisecond timestamp at which the local flag definitions were last successfully loaded,
    /// or <c>null</c> if they have not yet been loaded.
    /// </summary>
    public long? FlagDefinitionsLoadedAt
    {
        get
        {
            var value = Interlocked.Read(ref _flagDefinitionsLoadedAtMs);
            return value == 0 ? null : value;
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // Cancel the token so the polling loop exits, then wait for it to finish
        // (either by completing normally or via cancellation) before disposing resources.
        try
        {
            await _cancellationTokenSource.CancelAsync();
            await (_pollingTask ?? Task.CompletedTask);
        }
        finally
        {
            _timer.Dispose();
            _cancellationTokenSource.Dispose();
        }
    }

    public void Clear()
    {
        Interlocked.Exchange(ref _localEvaluator, null);
        Interlocked.Exchange(ref _etag, null);
        Interlocked.Exchange(ref _flagDefinitionsLoadedAtMs, 0);
    }
}

internal static partial class LocalFeatureFlagsLoaderLoggerExtensions
{

    [LoggerMessage(
        EventId = 110,
        Level = LogLevel.Trace,
        Message = "{MethodName} exiting due to OperationCancelled exception")]
    public static partial void LogTraceOperationCancelled(
        this ILogger<LocalFeatureFlagsLoader> logger,
        string methodName);

    [LoggerMessage(
        EventId = 500,
        Level = LogLevel.Error,
        Message = "Unexpected exception occurred while loading feature flags.")]
    public static partial void LogErrorUnexpectedException(this ILogger<LocalFeatureFlagsLoader> logger, Exception exception);

    [LoggerMessage(
        EventId = 501,
        Level = LogLevel.Warning,
        Message = "[FEATURE FLAGS] Quota exceeded, resetting feature flag data. Learn more about billing limits at https://posthog.com/docs/billing/limits-alerts")]
    public static partial void LogWarningQuotaExceeded(this ILogger<LocalFeatureFlagsLoader> logger, Exception e);

    [LoggerMessage(
        EventId = 502,
        Level = LogLevel.Warning,
        Message = "[FEATURE FLAGS] You are using local evaluation but {Count} flag(s) have experience continuity enabled "
                  + "and cannot be resolved locally (reason: experience_continuity): {FlagKeys}. Evaluating these flags "
                  + "makes a server request, or leaves them unresolved when OnlyEvaluateLocally is true. To evaluate them "
                  + "locally, disable experience continuity on these flags in PostHog.")]
    public static partial void LogWarningExperienceContinuityFlags(
        this ILogger<LocalFeatureFlagsLoader> logger,
        int count,
        string flagKeys);

    [LoggerMessage(
        EventId = 503,
        Level = LogLevel.Warning,
        Message = "[FEATURE FLAGS] You are using local evaluation but {Count} flag(s) use something local evaluation "
                  + "does not support, such as a static cohort, an unknown operator, or a missing or circular flag "
                  + "dependency (reason: unsupported_definition): {FlagKeys}. Evaluating these flags falls back to a "
                  + "server request when evaluation reaches that part, or leaves them unresolved when "
                  + "OnlyEvaluateLocally is true. To evaluate them locally, change these flags in PostHog or upgrade "
                  + "the PostHog .NET SDK.")]
    public static partial void LogWarningUnsupportedDefinitionFlags(
        this ILogger<LocalFeatureFlagsLoader> logger,
        int count,
        string flagKeys);
}