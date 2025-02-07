#if NETSTANDARD2_0 || NETSTANDARD2_1
namespace System.Threading;

internal static class CancellationTokenSourceExtensions
{
    public static Task CancelAsync(this CancellationTokenSource cancellationTokenSource)
    {
        cancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }
}
#endif