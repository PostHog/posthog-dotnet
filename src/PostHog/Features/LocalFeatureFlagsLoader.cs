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
internal sealed class LocalFeatureFlagsLoader(
    PostHogApiClient postHogApiClient,
    IOptions<PostHogOptions> options,
    ITaskScheduler taskScheduler,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory) : IDisposable
{
    volatile int _started;
    LocalEvaluator? _localEvaluator;
    string? _etag; // ETag for conditional requests to reduce bandwidth
    readonly CancellationTokenSource _cancellationTokenSource = new();
    readonly PeriodicTimer _timer = new(options.Value.FeatureFlagPollInterval, timeProvider);
    readonly ILogger<LocalFeatureFlagsLoader> _logger = loggerFactory.CreateLogger<LocalFeatureFlagsLoader>();
    readonly ILogger<LocalEvaluator> _localEvaluatorLogger = loggerFactory.CreateLogger<LocalEvaluator>();

    void StartPollingIfNotStarted()
    {
        // If we've started polling, don't start another poll.
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 1)
        {
            return;
        }
        taskScheduler.Run(() => PollForFeatureFlagsAsync(_cancellationTokenSource.Token));
    }

    /// <summary>
    /// Retrieves the feature flags from the local cache. If the cache is empty, it will fetch the flags from the API.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>All the feature flags.</returns>
    /// <exception cref="ApiException">Thrown when the API returns a <c>quota_limited</c> error.</exception>
    public async ValueTask<LocalEvaluator?> GetFeatureFlagsForLocalEvaluationAsync(CancellationToken cancellationToken)
    {
        if (options.Value.PersonalApiKey is null)
        {
            // Local evaluation is not enabled since it requires a personal api key.
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
        if (options.Value.PersonalApiKey is null)
        {
            return null;
        }

        try
        {
            return await LoadLocalEvaluatorAsync(cancellationToken);
        }
        catch (ApiException e) when (e.ErrorType is "quota_limited")
        {
            _etag = null; // Clear ETag on quota limit so next request starts fresh
            throw;
        }
    }

    async Task<LocalEvaluator?> LoadLocalEvaluatorAsync(CancellationToken cancellationToken)
    {
        StartPollingIfNotStarted();
        var response = await postHogApiClient.GetFeatureFlagsForLocalEvaluationAsync(_etag, cancellationToken);

        // Update ETag from response (preserve existing if not provided on 304)
        if (response.ETag is not null || !response.IsNotModified)
        {
            _etag = response.ETag;
        }

        // If 304 Not Modified, keep using cached data
        if (response.IsNotModified)
        {
            return _localEvaluator;
        }

        if (response.Result is null)
        {
            return _localEvaluator;
        }

        var localEvaluator = new LocalEvaluator(response.Result, timeProvider, _localEvaluatorLogger);
        Interlocked.Exchange(ref _localEvaluator, localEvaluator);
        return localEvaluator;
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
                    _etag = null; // Clear ETag on quota limit
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

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
        _timer.Dispose();
    }

    public void Clear()
    {
        Interlocked.Exchange(ref _localEvaluator, null);
        _etag = null;
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
}