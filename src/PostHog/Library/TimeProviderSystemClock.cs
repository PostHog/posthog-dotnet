using Microsoft.Extensions.Internal;
#if NETSTANDARD2_0 || NETSTANDARD2_1
using PostHog.Library.Polyfills;
#endif

namespace PostHog.Library;

/// <summary>
/// An implementation of <see cref="ISystemClock"/> that uses a
/// <see cref="TimeProvider"/> under the hood.
/// </summary>
internal class TimeProviderSystemClock(TimeProvider timeProvider) : ISystemClock
{
    /// <summary>
    /// Retrieve's the current system's time in UTC.
    /// </summary>
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}