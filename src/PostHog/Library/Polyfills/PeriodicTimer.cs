#if NETSTANDARD2_0 || NETSTANDARD2_1
using PostHog.Library.Polyfills;

namespace System.Threading;

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
internal sealed class PeriodicTimer : IDisposable
{
    private readonly ITimer _timer;
    private readonly object _lock = new object();
    private readonly TimeProvider _timeProvider;
    private TimeSpan _period;
    private TaskCompletionSource<bool>? _tcs;
    private bool _disposed;
    private bool _signaled;

    public PeriodicTimer(TimeSpan period) : this(period, TimeProvider.System)
    {
    }

    public PeriodicTimer(TimeSpan period, TimeProvider timeProvider)
    {
        if (!TryGetMilliseconds(period, out uint ms))
            throw new ArgumentOutOfRangeException(nameof(period));

        _period = period;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        TimerCallback callback = TimerCallback;

        if (_timeProvider == TimeProvider.System)
        {
            _timer = new TimerQueueTimer(callback, null, ms, ms, flowExecutionContext: false);
        }
        else
        {
            using (ExecutionContext.SuppressFlow())
            {
                _timer = _timeProvider.CreateTimer(callback, null, period, period);
            }
        }
    }

    public TimeSpan Period
    {
        get => _period;
        set
        {
            if (!TryGetMilliseconds(value, out uint ms))
                throw new ArgumentOutOfRangeException(nameof(value));

            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(PeriodicTimer));

                _period = value;
                if (!_timer.Change(value, value))
                    throw new ObjectDisposedException(nameof(PeriodicTimer));
            }
        }
    }

    public async ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool> tcs;
        lock (_lock)
        {
            if (_disposed)
                return false;

            if (_signaled)
            {
                _signaled = false;
                return true;
            }

            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs = _tcs;
        }

        using (cancellationToken.Register(() => tcs.TrySetCanceled()))
        {
            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _timer.Dispose();
            _tcs?.TrySetResult(false);
        }
    }

    private void TimerCallback(object? state)
    {
        lock (_lock)
        {
            if (_disposed || _tcs == null)
                return;

            _signaled = true;
            _tcs.TrySetResult(true);
        }
    }

    private static bool TryGetMilliseconds(TimeSpan value, out uint milliseconds)
    {
        long ms = (long)value.TotalMilliseconds;
        if ((ms >= 1 && ms <= TimerConstants.MaxSupportedTimeout) || value == Timeout.InfiniteTimeSpan)
        {
            milliseconds = (uint)ms;
            return true;
        }

        milliseconds = 0;
        return false;
    }
}
#endif