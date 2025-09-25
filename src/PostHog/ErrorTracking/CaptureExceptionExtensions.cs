using PostHog.Library;

namespace PostHog;
using static Ensure;

/// <summary>
/// Extensions of <see cref="IPostHogClient"/> related to capturing a specific type of events - exceptions.
/// </summary>
public static class CaptureExceptionExtensions
{
    /// <summary>
    /// Captures an exception event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="exception">The exception object that you want to capture.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <returns><c>true</c> if the exception event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureException(
        this IPostHogClient client,
        Exception exception,
        string distinctId)
        => NotNull(client).CaptureException(
            exception,
            distinctId,
            properties: null,
            groups: null,
            sendFeatureFlags: false);

    /// <summary>
    /// Captures an exception event with feature flags.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="exception">The exception object that you want to capture.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="sendFeatureFlags">Default: <c>false</c>. If <c>true</c>, feature flags are sent with the captured event.</param>
    /// <returns><c>true</c> if the exception event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureException(
        this IPostHogClient client,
        Exception exception,
        string distinctId,
        bool sendFeatureFlags)
        => NotNull(client).CaptureException(
            exception,
            distinctId,
            properties: null,
            groups: null,
            sendFeatureFlags: sendFeatureFlags);

    /// <summary>
    /// Captures an exception event with additional properties.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="exception">The exception object that you want to capture.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="properties">Optional: The properties to send along with the event.</param>
    /// <returns><c>true</c> if the exception event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureException(
        this IPostHogClient client,
        Exception exception,
        string distinctId,
        Dictionary<string, object>? properties)
        => NotNull(client).CaptureException(
            exception,
            distinctId,
            properties,
            groups: null,
            sendFeatureFlags: false);

    /// <summary>
    /// Captures an exception event with a custom timestamp.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="exception">The exception object that you want to capture.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="timestamp">The timestamp when the event occurred.</param>
    /// <returns><c>true</c> if the exception event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureException(
        this IPostHogClient client,
        Exception exception,
        string distinctId,
        DateTimeOffset timestamp)
        => NotNull(client).CaptureException(
            exception,
            distinctId,
            properties: null,
            groups: null,
            sendFeatureFlags: false,
            timestamp: timestamp);

    /// <summary>
    /// Captures an exception event with a custom timestamp and additional properties.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="exception">The exception object that you want to capture.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="timestamp">The timestamp when the event occurred.</param>
    /// <param name="properties">Optional: The properties to send along with the event.</param>
    /// <returns><c>true</c> if the exception event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureException(
        this IPostHogClient client,
        Exception exception,
        string distinctId,
        DateTimeOffset timestamp,
        Dictionary<string, object>? properties)
        => NotNull(client).CaptureException(
            exception,
            distinctId,
            properties,
            groups: null,
            sendFeatureFlags: false,
            timestamp: timestamp);

    /// <summary>
    /// Captures an exception event with a custom timestamp and groups.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="exception">The exception object that you want to capture.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="timestamp">The timestamp when the event occurred.</param>
    /// <param name="groups">A set of groups to send with the event. The groups are identified by their group_type and group_key.</param>
    /// <returns><c>true</c> if the exception event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureException(
        this IPostHogClient client,
        Exception exception,
        string distinctId,
        DateTimeOffset timestamp,
        GroupCollection groups)
        => NotNull(client).CaptureException(
            exception,
            distinctId,
            properties: null,
            groups,
            sendFeatureFlags: false,
            timestamp: timestamp);

    /// <summary>
    /// Captures an exception event with a custom timestamp, properties, and groups.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="exception">The exception object that you want to capture.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="timestamp">The timestamp when the event occurred.</param>
    /// <param name="properties">Optional: The properties to send along with the event.</param>
    /// <param name="groups">Optional: A set of groups to send with the event. The groups are identified by their group_type and group_key.</param>
    /// <returns><c>true</c> if the exception event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureException(
        this IPostHogClient client,
        Exception exception,
        string distinctId,
        DateTimeOffset timestamp,
        Dictionary<string, object>? properties,
        GroupCollection? groups)
        => NotNull(client).CaptureException(
            exception,
            distinctId,
            properties,
            groups,
            sendFeatureFlags: false,
            timestamp: timestamp);

    /// <summary>
    /// Captures an exception event with a custom timestamp and feature flags.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="exception">The exception object that you want to capture.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="timestamp">The timestamp when the event occurred.</param>
    /// <param name="sendFeatureFlags">Default: <c>false</c>. If <c>true</c>, feature flags are sent with the captured event.</param>
    /// <returns><c>true</c> if the exception event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureException(
        this IPostHogClient client,
        Exception exception,
        string distinctId,
        DateTimeOffset timestamp,
        bool sendFeatureFlags)
        => NotNull(client).CaptureException(
            exception,
            distinctId,
            properties: null,
            groups: null,
            sendFeatureFlags: sendFeatureFlags,
            timestamp: timestamp);

    /// <summary>
    /// Captures an exception event with a custom timestamp, properties, groups, and feature flags.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="exception">The exception object that you want to capture.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="timestamp">The timestamp when the event occurred.</param>
    /// <param name="properties">Optional: The properties to send along with the event.</param>
    /// <param name="groups">Optional: A set of groups to send with the event. The groups are identified by their group_type and group_key.</param>
    /// <param name="sendFeatureFlags">Default: <c>false</c>. If <c>true</c>, feature flags are sent with the captured event.</param>
    /// <returns><c>true</c> if the exception event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureException(
        this IPostHogClient client,
        Exception exception,
        string distinctId,
        DateTimeOffset timestamp,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        bool sendFeatureFlags)
        => NotNull(client).CaptureException(
            exception,
            distinctId,
            properties,
            groups,
            sendFeatureFlags,
            timestamp);
}
