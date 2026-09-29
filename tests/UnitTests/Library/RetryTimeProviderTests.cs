#if NET8_0_OR_GREATER
using System.Net;
using System.Net.Sockets;
using PostHog;
using PostHog.Api;
using PostHog.Library;

namespace HttpClientExtensionsTests;

public class RetryTimeProviderTests
{
    [Fact]
    public async Task WaitsForTimerRegistrationBeforeAdvancingClock()
    {
        var handler = new FakeRetryHttpMessageHandler();
        handler.AddException(new HttpRequestException("Connection reset", new SocketException((int)SocketError.ConnectionReset)));
        handler.AddResponse(HttpStatusCode.OK, new { flags = new { } });
        using var httpClient = new HttpClient(handler);
        var clock = new PausedTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var request = Task.Run(() => httpClient.PostJsonWithNetworkRetryAsync<FlagsApiResult>(
            new Uri("https://us.i.posthog.com/flags/?v=2"),
            new { api_key = "test", distinct_id = "user-1" },
            clock,
            new PostHogOptions { ProjectToken = "test", InitialRetryDelay = TimeSpan.FromMilliseconds(1) },
            new FeatureFlagRequestCircuitBreaker(),
            cancellation.Token));
        try
        {
            await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await handler.WaitForRequestCountAsync(1);
            Assert.Equal(1, handler.RequestCount);
            var readyToAdvance = clock.WaitForRetryAsync(handler, 1);
            Assert.False(readyToAdvance.IsCompleted);
            clock.Release.Set();
            await readyToAdvance;
            clock.Advance(TimeSpan.FromMilliseconds(1));
            var result = await request.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(result);
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            clock.Release.Set();
            await cancellation.CancelAsync();
            try
            {
                await request.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }
            clock.Release.Dispose();
        }
    }

    sealed class PausedTimeProvider : RetryTimeProvider
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Entered.TrySetResult(true);
            if (!Release.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Timer-registration barrier was not released.");
            }
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
#endif
