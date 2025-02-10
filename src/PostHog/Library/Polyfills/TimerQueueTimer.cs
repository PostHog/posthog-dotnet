#if NETSTANDARD2_0 || NETSTANDARD2_1


namespace PostHog.Library.Polyfills;

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

internal sealed class TimerQueueTimer : ITimer
{
    private readonly TimerQueue _associatedTimerQueue;
    internal TimerQueueTimer? _next;
    internal TimerQueueTimer? _prev;
    internal bool _short;
    internal long _startTicks;
    internal uint _dueTime;
    internal uint _period;
    private readonly TimerCallback _timerCallback;
    private readonly object? _state;
    private readonly ExecutionContext? _executionContext;
    private bool _canceled;

    internal TimerQueueTimer(TimerCallback timerCallback, object? state, uint dueTime, uint period,
        bool flowExecutionContext)
    {
        _timerCallback = timerCallback;
        _state = state;
        _dueTime = Timeout.UnsignedInfinite;
        _period = Timeout.UnsignedInfinite;

        if (flowExecutionContext)
        {
            _executionContext = ExecutionContext.Capture();
        }

        _associatedTimerQueue = TimerQueue.Instances[0];

        if (dueTime != Timeout.UnsignedInfinite)
            Change(dueTime, period);
    }

    public bool Change(uint dueTime, uint period)
    {
        lock (_associatedTimerQueue.SharedLock)
        {
            if (_canceled) return false;
            _period = period;

            if (dueTime == Timeout.UnsignedInfinite)
            {
                _associatedTimerQueue.DeleteTimer(this);
            }
            else
            {
                _associatedTimerQueue.UpdateTimer(this, dueTime, period);
            }
        }

        return true;
    }

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        return Change(GetMilliseconds(dueTime), GetMilliseconds(period));
    }

    private static uint GetMilliseconds(TimeSpan time)
    {
        long ms = (long)time.TotalMilliseconds;
        return (uint)Math.Max(0, Math.Min(ms, TimerConstants.MaxSupportedTimeout));
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }

    public void Dispose()
    {
        lock (_associatedTimerQueue.SharedLock)
        {
            if (!_canceled)
            {
                _canceled = true;
                _associatedTimerQueue.DeleteTimer(this);
            }
        }
    }
}
#endif