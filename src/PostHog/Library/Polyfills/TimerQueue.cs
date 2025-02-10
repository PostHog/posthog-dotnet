#if NETSTANDARD2_0 || NETSTANDARD2_1


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using PostHog.Library.Polyfills;

namespace PostHog.Library.Polyfills;

internal sealed class TimerQueue
{
    public static readonly TimerQueue[] Instances = CreateTimerQueues();
    private TimerQueueTimer? _shortTimers;
    private TimerQueueTimer? _longTimers;
    private long _currentAbsoluteThreshold = TickCount64 + ShortTimersThresholdMilliseconds;

    private static TimerQueue[] CreateTimerQueues()
    {
        return new[] { new TimerQueue(0) }; // Single queue for .NET Standard
    }

    public long ActiveCount { get; private set; }
    private const int ShortTimersThresholdMilliseconds = 333;

    internal Lock SharedLock { get; } = new Lock();

    private TimerQueue(int _) { }

    internal static long TickCount64 => DateTime.UtcNow.Ticks;

    public bool UpdateTimer(TimerQueueTimer timer, uint dueTime, uint period)
    {
        long nowTicks = TickCount64;
        bool shouldBeShort = _currentAbsoluteThreshold - (nowTicks + dueTime) >= 0;

        if (timer._dueTime == Timeout.UnsignedInfinite)
        {
            timer._short = shouldBeShort;
            LinkTimer(timer);
            ++ActiveCount;
        }
        else if (timer._short != shouldBeShort)
        {
            UnlinkTimer(timer);
            timer._short = shouldBeShort;
            LinkTimer(timer);
        }

        timer._dueTime = dueTime;
        timer._period = (period == 0) ? Timeout.UnsignedInfinite : period;
        timer._startTicks = nowTicks;
        return true;
    }

    private void LinkTimer(TimerQueueTimer timer)
    {
        ref TimerQueueTimer? listHead = ref timer._short ? ref _shortTimers : ref _longTimers;
        timer._next = listHead;
        if (timer._next != null)
        {
            timer._next._prev = timer;
        }
        timer._prev = null;
        listHead = timer;
    }

    private void UnlinkTimer(TimerQueueTimer timer)
    {
        TimerQueueTimer? t = timer._next;
        if (t != null)
        {
            t._prev = timer._prev;
        }

        if (_shortTimers == timer)
        {
            _shortTimers = t;
        }
        else if (_longTimers == timer)
        {
            _longTimers = t;
        }

        t = timer._prev;
        if (t != null)
        {
            t._next = timer._next;
        }
    }

    public void DeleteTimer(TimerQueueTimer timer)
    {
        if (timer._dueTime != Timeout.UnsignedInfinite)
        {
            --ActiveCount;
            UnlinkTimer(timer);
            timer._prev = null;
            timer._next = null;
            timer._dueTime = Timeout.UnsignedInfinite;
            timer._period = Timeout.UnsignedInfinite;
            timer._startTicks = 0;
            timer._short = false;
        }
    }
}

#endif
