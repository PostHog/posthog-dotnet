using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog.Api;
using PostHog.Config;
using PostHog.Library;

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
    ILogger logger) : IDisposable
{
    volatile int _started;
    LocalEvaluator? _localEvaluator;
    readonly CancellationTokenSource _cancellationTokenSource = new();
    readonly PeriodicTimer _timer = new(options.Value.FeatureFlagPollInterval, timeProvider);

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

        var localEvaluator = new LocalEvaluator(newApiResult, timeProvider, logger);
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
                    logger.LogErrorUnexpectedException(e);
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogTraceOperationCancelled(nameof(PollForFeatureFlagsAsync));
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