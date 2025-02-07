#if NETSTANDARD2_0 || NETSTANDARD2_1
namespace PostHog.Library.Polyfills;

using System;
using System.Threading;
using System.Threading.Tasks;

internal static class TaskExtensions
{
    /// <summary>
    /// Waits for a <see cref="ValueTask{bool}"/> to complete within the specified timeout.
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
    /// Waits for a <see cref="Task{bool}"/> to complete within the specified timeout.
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
        var delayTask = Task.Delay(Timeout.Infinite, cancellationToken);

        var completedTask = await Task.WhenAny(task, delayTask);
        if (completedTask == delayTask)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        await task; // Ensure exception is thrown if the original task failed
    }
}

#endif