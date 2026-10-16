#if NET8_0_OR_GREATER
using System.Diagnostics;

namespace PostHog;

internal static class PostHogActivityContext
{
    internal static void Apply(Activity? activity)
    {
        var context = PostHogContext.Current;
        if (activity is null || context is null || !context.IsActive)
        {
            return;
        }

        AddIfMissing(activity, "posthogDistinctId", context.DistinctId);
        AddIfMissing(activity, "sessionId", context.SessionId);
    }

    static void AddIfMissing(Activity activity, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value) && activity.GetTagItem(key) is null)
        {
            activity.SetTag(key, value);
        }
    }
}
#endif
