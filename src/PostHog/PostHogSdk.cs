using System.Text.Json;
using PostHog.Api;
using PostHog.Features;
using PostHog.Json;

namespace PostHog;

/// <summary>
/// Static convenience facade that delegates calls to a process-wide default <see cref="IPostHogClient"/>.
/// </summary>
/// <remarks>
/// Prefer dependency injection and <see cref="IPostHogClient"/> for applications that already use DI. This facade is
/// intended for console apps, scripts, and other places where passing a client instance around is inconvenient.
/// </remarks>
public static class PostHogSdk
{
    static IPostHogClient? _defaultClient;

    /// <summary>
    /// Gets or sets the process-wide default PostHog client used by the static facade methods.
    /// </summary>
    /// <remarks>
    /// Setting this property does not dispose the previous client. Use <see cref="ShutdownAsync"/> to flush, dispose,
    /// and clear the current default client when the application is shutting down.
    /// </remarks>
    public static IPostHogClient? DefaultClient
    {
        get => Volatile.Read(ref _defaultClient);
        set => Volatile.Write(ref _defaultClient, value);
    }

    /// <summary>
    /// Creates a <see cref="PostHogClient"/>, stores it as <see cref="DefaultClient"/>, and returns it.
    /// </summary>
    /// <param name="options">The options used to configure the client.</param>
    /// <returns>The created <see cref="IPostHogClient"/>.</returns>
    public static IPostHogClient Init(PostHogOptions options)
    {
        var client = new PostHogClient(options);
        DefaultClient = client;
        return client;
    }

    /// <summary>
    /// Captures an event using the default client.
    /// </summary>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="eventName">Human friendly name of the event.</param>
    /// <returns><c>true</c> if the event was successfully enqueued; otherwise <c>false</c>.</returns>
    public static bool Capture(string distinctId, string eventName)
        => Current.Capture(distinctId, eventName);

    /// <summary>
    /// Captures an event with additional properties using the default client.
    /// </summary>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="eventName">Human friendly name of the event.</param>
    /// <param name="properties">Optional properties to send along with the event.</param>
    /// <returns><c>true</c> if the event was successfully enqueued; otherwise <c>false</c>.</returns>
    public static bool Capture(string distinctId, string eventName, Dictionary<string, object>? properties)
        => Current.Capture(distinctId, eventName, properties);

    /// <summary>
    /// Captures an event using the default client.
    /// </summary>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="eventName">Human friendly name of the event.</param>
    /// <param name="properties">Optional properties to send along with the event.</param>
    /// <param name="groups">Optional groups related to this event.</param>
    /// <param name="sendFeatureFlags">Whether to send feature flag data with the event.</param>
    /// <param name="timestamp">Optional timestamp when the event occurred.</param>
    /// <returns><c>true</c> if the event was successfully enqueued; otherwise <c>false</c>.</returns>
#pragma warning disable CS0618
    public static bool Capture(
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        bool sendFeatureFlags,
        DateTimeOffset? timestamp = null)
        => Current.Capture(distinctId, eventName, properties, groups, sendFeatureFlags, timestamp);
#pragma warning restore CS0618

    /// <summary>
    /// Captures an exception using the default client.
    /// </summary>
    /// <param name="exception">The exception to capture.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <returns><c>true</c> if the exception event was successfully enqueued; otherwise <c>false</c>.</returns>
    public static bool CaptureException(Exception exception, string distinctId)
        => Current.CaptureException(exception, distinctId);

    /// <summary>
    /// Captures an exception using the default client.
    /// </summary>
    /// <param name="exception">The exception to capture.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="properties">Optional properties to send along with the event.</param>
    /// <param name="groups">Optional groups related to this event.</param>
    /// <param name="sendFeatureFlags">Whether to send feature flag data with the event.</param>
    /// <param name="timestamp">Optional timestamp when the event occurred.</param>
    /// <returns><c>true</c> if the exception event was successfully enqueued; otherwise <c>false</c>.</returns>
#pragma warning disable CS0618
    public static bool CaptureException(
        Exception exception,
        string distinctId,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        bool sendFeatureFlags,
        DateTimeOffset? timestamp = null)
        => Current.CaptureException(exception, distinctId, properties, groups, sendFeatureFlags, timestamp);
#pragma warning restore CS0618

    /// <summary>
    /// Identifies a user using the default client.
    /// </summary>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="personPropertiesToSet">Properties to set on the user profile.</param>
    /// <param name="personPropertiesToSetOnce">Properties to set only once on the user profile.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An <see cref="ApiResult"/> with the result of the operation.</returns>
    public static Task<ApiResult> IdentifyAsync(
        string distinctId,
        Dictionary<string, object>? personPropertiesToSet = null,
        Dictionary<string, object>? personPropertiesToSetOnce = null,
        CancellationToken cancellationToken = default)
        => Current.IdentifyAsync(distinctId, personPropertiesToSet, personPropertiesToSetOnce, cancellationToken);

    /// <summary>
    /// Creates an alias using the default client.
    /// </summary>
    /// <param name="previousId">The anonymous or temporary identifier you were using for the user.</param>
    /// <param name="newId">The identifier for the known user.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An <see cref="ApiResult"/> with the result of the operation.</returns>
    public static Task<ApiResult> AliasAsync(
        string previousId,
        string newId,
        CancellationToken cancellationToken = default)
        => Current.AliasAsync(previousId, newId, cancellationToken);

    /// <summary>
    /// Sets group properties using the default client.
    /// </summary>
    /// <param name="type">Type of group.</param>
    /// <param name="key">Unique identifier for that type of group.</param>
    /// <param name="properties">Additional information about the group.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An <see cref="ApiResult"/> with the result of the operation.</returns>
    public static Task<ApiResult> GroupIdentifyAsync(
        string type,
        StringOrValue<int> key,
        Dictionary<string, object>? properties = null,
        CancellationToken cancellationToken = default)
        => Current.GroupIdentifyAsync(type, key, properties, cancellationToken);

    /// <summary>
    /// Sets group properties using the default client.
    /// </summary>
    /// <param name="distinctId">The identifier you use for the current user.</param>
    /// <param name="type">Type of group.</param>
    /// <param name="key">Unique identifier for that type of group.</param>
    /// <param name="properties">Additional information about the group.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An <see cref="ApiResult"/> with the result of the operation.</returns>
    public static Task<ApiResult> GroupIdentifyAsync(
        string distinctId,
        string type,
        StringOrValue<int> key,
        Dictionary<string, object>? properties = null,
        CancellationToken cancellationToken = default)
        => Current.GroupIdentifyAsync(distinctId, type, key, properties, cancellationToken);

    /// <summary>
    /// Determines whether a feature is enabled using the default client.
    /// </summary>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="options">Optional options used to control feature flag evaluation.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns><c>true</c> if the feature is enabled for the user; otherwise <c>false</c>.</returns>
#pragma warning disable CS0618
    public static Task<bool> IsFeatureEnabledAsync(
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options = null,
        CancellationToken cancellationToken = default)
        => Current.IsFeatureEnabledAsync(featureKey, distinctId, options, cancellationToken);
#pragma warning restore CS0618

    /// <summary>
    /// Retrieves a feature flag using the default client.
    /// </summary>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="options">Optional options used to control feature flag evaluation.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The feature flag or <c>null</c> if it does not exist or is not enabled.</returns>
#pragma warning disable CS0618
    public static Task<FeatureFlag?> GetFeatureFlagAsync(
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options = null,
        CancellationToken cancellationToken = default)
        => Current.GetFeatureFlagAsync(featureKey, distinctId, options, cancellationToken);
#pragma warning restore CS0618

    /// <summary>
    /// Retrieves all feature flags using the default client.
    /// </summary>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="options">Optional options used to control feature flag evaluation.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>A dictionary containing all feature flags.</returns>
    public static Task<IReadOnlyDictionary<string, FeatureFlag>> GetAllFeatureFlagsAsync(
        string distinctId,
        AllFeatureFlagsOptions? options = null,
        CancellationToken cancellationToken = default)
        => Current.GetAllFeatureFlagsAsync(distinctId, options, cancellationToken);

    /// <summary>
    /// Retrieves a remote config payload using the default client.
    /// </summary>
    /// <param name="key">The remote config key.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The remote config payload or <c>null</c>.</returns>
    public static Task<JsonDocument?> GetRemoteConfigPayloadAsync(
        string key,
        CancellationToken cancellationToken = default)
        => Current.GetRemoteConfigPayloadAsync(key, cancellationToken);

    /// <summary>
    /// Loads or reloads feature flag definitions for local evaluation using the default client.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static Task LoadFeatureFlagsAsync(CancellationToken cancellationToken = default)
        => Current.LoadFeatureFlagsAsync(cancellationToken);

    /// <summary>
    /// Flushes the event queue on the default client.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static Task FlushAsync()
        => Current.FlushAsync();

    /// <summary>
    /// Flushes, disposes, and clears the current default client if one is configured.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    public static async ValueTask ShutdownAsync()
    {
        var client = Interlocked.Exchange(ref _defaultClient, null);
        if (client is null)
        {
            return;
        }

        try
        {
            await client.FlushAsync();
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    static IPostHogClient Current
    {
        get
        {
            var client = DefaultClient;
            if (client is not null)
            {
                return client;
            }

            NoOpPostHogClient.LogNoDefaultClient();
            return NoOpPostHogClient.Instance;
        }
    }
}
