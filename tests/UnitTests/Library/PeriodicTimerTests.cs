#if NETCOREAPP3_1
namespace PeriodicTimerTests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Time.Testing;
using PostHog.Library.Polyfills;

public class TheWaitForNextTickAsyncMethod
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task TimerTicksAtExpectedInterval()
    {
        var period = TimeSpan.FromMilliseconds(100);
        using var timer = new PeriodicTimer(period, _timeProvider);

        // Timer should not tick yet
        Assert.False(await timer.WaitForNextTickAsync().WaitAsync(TimeSpan.FromMilliseconds(10)));

        // Advance time by one period
        _timeProvider.Advance(period);
        Assert.True(await timer.WaitForNextTickAsync().WaitAsync(TimeSpan.FromMilliseconds(10)));

        // Advance time again
        _timeProvider.Advance(period);
        Assert.True(await timer.WaitForNextTickAsync().WaitAsync(TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public async Task TimerStopsWhenDisposed()
    {
        var period = TimeSpan.FromMilliseconds(100);
        var timer = new PeriodicTimer(period, _timeProvider);

        // Advance time and wait for a tick
        _timeProvider.Advance(period);
        Assert.True(await timer.WaitForNextTickAsync().WaitAsync(TimeSpan.FromMilliseconds(10)));

        // Dispose the timer
        timer.Dispose();

        // Timer should return false after being disposed
        Assert.False(await timer.WaitForNextTickAsync());
    }

    [Fact]
    public async Task TimerWaitCanBeCancelled()
    {
        var period = TimeSpan.FromMilliseconds(100);
        using var timer = new PeriodicTimer(period, _timeProvider);
        using var cts = new CancellationTokenSource();

        // Start waiting but cancel it before advancing time
        var waitTask = timer.WaitForNextTickAsync(cts.Token);
        cts.Cancel();

        // Ensure the task is canceled
        var result = await waitTask;
        Assert.False(result);
    }

    [Fact]
    public async Task TimerPeriodCanBeChanged()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(300), _timeProvider);

        // Advance by initial period
        _timeProvider.Advance(TimeSpan.FromMilliseconds(300));
        Assert.True(await timer.WaitForNextTickAsync().WaitAsync(TimeSpan.FromMilliseconds(10)));

        // Change period to a shorter duration
        timer.Period = TimeSpan.FromMilliseconds(100);

        // Advance by new period
        _timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(await timer.WaitForNextTickAsync().WaitAsync(TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public async Task TimerDoesNotTickAfterDispose()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100), _timeProvider);

        // Advance and consume one tick
        _timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(await timer.WaitForNextTickAsync().WaitAsync(TimeSpan.FromMilliseconds(10)));

        // Dispose the timer
        timer.Dispose();

        // Timer should return false immediately
        Assert.False(await timer.WaitForNextTickAsync());
    }
}


#endif