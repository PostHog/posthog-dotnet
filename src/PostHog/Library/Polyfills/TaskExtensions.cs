#if NETSTANDARD2_0 || NETSTANDARD2_1
namespace PostHog.Library.Polyfills;

using System;
using System.Threading;
using System.Threading.Tasks;

internal static class TaskExtensions
{
    /// <summary>
    /// Waits for a <see cref="ValueTask{TResult}"/> returning <see cref="bool"/> to complete within the specified timeout.
    /// If the task does not complete within the timeout, it returns false.
    /// </summary>
    public static async Task<bool> WaitAsync(this ValueTask<bool> valueTask, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await valueTask.AsTask().WaitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false; // Timeout occurred
        }
    }

    /// <summary>
    /// Waits for a <see cref="Task{TResult}"/> returning <see cref="bool"/> to complete within the specified timeout.
    /// If the task does not complete within the timeout, it returns false.
    /// </summary>
    public static async Task<bool> WaitAsync(this Task<bool> task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await task.WaitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false; // Timeout occurred
        }
    }

    /// <summary>
    /// Waits for a <see cref="Task"/> to complete within the given timeout.
    /// If the timeout is exceeded, throws a <see cref="TimeoutException"/>.
    /// </summary>
    public static async Task WaitAsync(this Task task, CancellationToken cancellationToken)
    {
        // The delay listens on a linked source, not on the caller's token directly, so disposing the source
        // releases the registration when the task wins. Otherwise a long-lived token keeps one registration
        // per completed wait until it is cancelled.
        using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(Timeout.Infinite, delayCancellation.Token);

        var completedTask = await Task.WhenAny(task, delayTask);
        if (completedTask == delayTask)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        delayCancellation.Cancel();
        await task; // Ensure exception is thrown if the original task failed
    }

    /// <summary>
    /// Waits for a <see cref="Task{TResult}"/> to complete, or throws <see cref="OperationCanceledException"/>
    /// when <paramref name="cancellationToken"/> is cancelled first. The task itself keeps running.
    /// </summary>
    public static async Task<TResult> WaitAsync<TResult>(this Task<TResult> task, CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            await ((Task)task).WaitAsync(cancellationToken);
        }

        return await task;
    }
}

#endif