#if NETSTANDARD2_0 || NETSTANDARD2_1
using System.Runtime.CompilerServices;

namespace PostHog.Library.Polyfills;

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


/// <summary>
/// A high-performance mutual exclusion lock similar to .NET's internal Lock class.
/// </summary>
internal sealed class Lock
{
    private int _state; // 0 = unlocked, 1 = locked
    private readonly object _waiters = new object();

    /// <summary>Acquires the lock.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enter()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            WaitForLock();
        }
    }

    /// <summary>Attempts to acquire the lock.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnter()
    {
        return Interlocked.CompareExchange(ref _state, 1, 0) == 0;
    }

    /// <summary>Releases the lock.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Exit()
    {
        Volatile.Write(ref _state, 0);
        lock (_waiters)
        {
            Monitor.Pulse(_waiters);
        }
    }

    /// <summary>Waits for the lock if it's already held.</summary>
    private void WaitForLock()
    {
        lock (_waiters)
        {
            while (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                Monitor.Wait(_waiters);
            }
        }
    }
}

#endif