using System.Diagnostics;

namespace PostHog;

internal sealed class PostHogActivityListener : IDisposable
{
    readonly ActivityListener _listener = new()
    {
        ShouldListenTo = _ => true,
        ActivityStarted = PostHogActivityContext.Apply,
    };

    public PostHogActivityListener()
    {
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();
}
