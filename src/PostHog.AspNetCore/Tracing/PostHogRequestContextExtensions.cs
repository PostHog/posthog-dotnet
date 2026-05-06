using PostHog.Features;
using static PostHog.Library.Ensure;

namespace PostHog;

/// <summary>
/// Extension methods that use the current ASP.NET Core PostHog request context.
/// </summary>
public static class PostHogRequestContextExtensions
{
    /// <summary>
    /// Captures an event using the current request context distinct ID, or as a personless event if none is set.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="eventName">Human friendly name of the event.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool Capture(
        this IPostHogClient client,
        string eventName)
    {
        var checkedClient = NotNull(client);
        var context = PostHogContextHelper.ResolveCaptureContext(distinctId: null, properties: null);
        return checkedClient.Capture(
            context.DistinctId,
            eventName,
            context.Properties,
            groups: null,
            flags: null);
    }

    /// <summary>
    /// Captures an event using the current request context distinct ID, or as a personless event if none is set.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="eventName">Human friendly name of the event.</param>
    /// <param name="properties">Optional: The properties to send along with the event.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool Capture(
        this IPostHogClient client,
        string eventName,
        Dictionary<string, object>? properties)
    {
        var checkedClient = NotNull(client);
        var context = PostHogContextHelper.ResolveCaptureContext(distinctId: null, properties);
        return checkedClient.Capture(
            context.DistinctId,
            eventName,
            context.Properties,
            groups: null,
            flags: null);
    }

    /// <summary>
    /// Captures an exception using the current request context distinct ID, or as a personless event if none is set.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="exception">The exception to capture.</param>
    /// <param name="properties">Optional: The properties to send along with the event.</param>
    /// <returns><c>true</c> if the exception event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureException(
        this IPostHogClient client,
        Exception exception,
        Dictionary<string, object>? properties = null)
    {
        var checkedClient = NotNull(client);
        var context = PostHogContextHelper.ResolveCaptureContext(distinctId: null, properties);
        return checkedClient.CaptureException(
            exception,
            context.DistinctId,
            context.Properties,
            groups: null,
            flags: null);
    }

    /// <summary>
    /// Evaluates all feature flags using the current request context distinct ID and returns a <see cref="FeatureFlagEvaluations"/> snapshot.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <returns>A snapshot of feature flag evaluations.</returns>
    public static Task<FeatureFlagEvaluations> EvaluateFlagsAsync(this IPostHogClient client)
        => NotNull(client).EvaluateFlagsAsync(
            PostHogContextHelper.ResolveDistinctId() ?? string.Empty,
            options: null,
            CancellationToken.None);

    /// <summary>
    /// Evaluates all feature flags using the current request context distinct ID and returns a <see cref="FeatureFlagEvaluations"/> snapshot.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="options">Options used to control feature flag evaluation. <see cref="AllFeatureFlagsOptions.FlagKeysToEvaluate"/> scopes the underlying <c>/flags</c> request body.</param>
    /// <returns>A snapshot of feature flag evaluations.</returns>
    public static Task<FeatureFlagEvaluations> EvaluateFlagsAsync(
        this IPostHogClient client,
        AllFeatureFlagsOptions options)
        => NotNull(client).EvaluateFlagsAsync(
            PostHogContextHelper.ResolveDistinctId() ?? string.Empty,
            options,
            CancellationToken.None);
}
