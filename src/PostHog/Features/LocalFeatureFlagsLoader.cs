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

    async Task<LocalEvaluator?> LoadLocalEvaluatorAsync(CancellationToken cancellationToken)
    {
        StartPollingIfNotStarted();
        var newApiResult = await postHogApiClient.GetFeatureFlagsForLocalEvaluationAsync(cancellationToken);

        if (newApiResult is null)
        {
            return _localEvaluator;
        }

        var localEvaluator = new LocalEvaluator(newApiResult, timeProvider, _localEvaluatorLogger);
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
                catch (HttpRequestException e)
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

    public void Clear() => Interlocked.Exchange(ref _localEvaluator, null);
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
}