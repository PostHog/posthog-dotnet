#if NETSTANDARD2_0 || NETSTANDARD2_1
namespace PostHog.Library.Polyfills;

// A constant used by methods that take a timeout (Object.Wait, Thread.Sleep
// etc) to indicate that no timeout should occur.
//
internal static class Timeout
{
    public static readonly TimeSpan InfiniteTimeSpan = new TimeSpan(0, 0, 0, 0, Infinite);

    public const int Infinite = -1;
    internal const uint UnsignedInfinite = unchecked((uint)-1);
}
#endif