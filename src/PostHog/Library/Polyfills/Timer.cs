#if NETSTANDARD2_0 || NETSTANDARD2_1
namespace PostHog.Library.Polyfills;

internal sealed class TimerConstants
{
    internal const uint MaxSupportedTimeout = 0xfffffffe;
}
#endif