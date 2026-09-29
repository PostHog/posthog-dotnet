using Microsoft.Extensions.Time.Testing;
#if NET8_0_OR_GREATER
using System.Threading.Channels;
#endif

namespace HttpClientExtensionsTests;

class RetryTimeProvider : FakeTimeProvider
{
#if NET8_0_OR_GREATER
    readonly Channel<bool> _registeredTimers = Channel.CreateUnbounded<bool>();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        _registeredTimers.Writer.TryWrite(true);
        return timer;
    }
#endif

    public async Task WaitForRetryAsync(FakeRetryHttpMessageHandler handler, int requestCount)
    {
#if NET8_0_OR_GREATER
        // The request counter is incremented before the retry delay is registered.
        await _registeredTimers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
#else
        // The netstandard build uses real Task.Delay rather than the injected clock.
        await handler.WaitForRequestCountAsync(requestCount);
#endif
    }
}
