using PostHog.Api;
using PostHog.Features;
using PostHog.Json;

namespace PostHog;

/// <summary>
/// Interface for the PostHog client. This is the main interface for interacting with PostHog.
/// Use this to identify users and capture events.
/// </summary>
public interface IPostHogClient : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Identifies a user with the specified distinct ID and user properties.
    /// See <seealso href="https://posthog.com/docs/getting-started/identify-users"/>.
    /// </summary>
    /// <remarks>
    /// When you call Identify for a user, PostHog creates a
    /// <see href="https://posthog.com/docs/data/persons">Person Profile</see> of that user. You can use these person
    /// properties to better capture, analyze, and utilize user data. Whenever possible, we recommend passing in all
    /// person properties you have available each time you call identify, as this ensures their person profile on
    /// PostHog is up to date.
    /// </remarks>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="userPropertiesToSet">
    /// Key value pairs to store as a property of the user. Any key value pairs in this dictionary that match
    /// existing property keys will overwrite those properties.
    /// </param>
    /// <param name="userPropertiesToSetOnce">User properties to set only once (ex: Sign up date). If a property already exists, then the
    /// value in this dictionary is ignored.
    /// </param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An <see cref="ApiResult"/> with the result of the operation.</returns>
    Task<ApiResult> IdentifyPersonAsync(
        string distinctId,
        Dictionary<string, object>? userPropertiesToSet,
        Dictionary<string, object>? userPropertiesToSetOnce,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets a groups properties, which allows asking questions like "Who are the most active companies"
    /// using my product in PostHog.
    /// </summary>
    /// <param name="type">Type of group (ex: 'company'). Limited to 5 per project</param>
    /// <param name="key">Unique identifier for that type of group (ex: 'id:5')</param>
    /// <param name="properties">Additional information about the group.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An <see cref="ApiResult"/> with the result of the operation.</returns>
    Task<ApiResult> IdentifyGroupAsync(
        string type,
        StringOrValue<int> key,
        Dictionary<string, object>? properties,
        CancellationToken cancellationToken);

    /// <summary>
    /// Captures an event.
    /// </summary>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="eventName">Human friendly name of the event. Recommended format [object] [verb] such as "Project created" or "User signed up".</param>
    /// <param name="properties">Optional: The properties to send along with the event.</param>
    /// <param name="groups">Optional: Context of what groups are related to this event, example: { ["company"] = "id:5" }. Can be used to analyze companies instead of users.</param>
    /// <param name="sendFeatureFlags">Default: <c>false</c>. If <c>true</c>, feature flags are sent with the captured event.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    bool CaptureEvent(
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        bool sendFeatureFlags);

    /// <summary>
    /// Determines whether a feature is enabled for the specified user.
    /// </summary>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="options">Optional: Options used to control feature flag evaluation.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>
    /// <c>true</c> if the feature is enabled for the user. <c>false</c> if not. <c>null</c> if the feature is undefined.
    /// </returns>
    Task<bool?> IsFeatureEnabledAsync(
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a feature flag.
    /// </summary>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="options">Optional: Options used to control feature flag evaluation.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The feature flag or null if it does not exist or is not enabled.</returns>
    Task<FeatureFlag?> GetFeatureFlagAsync(
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all the feature flags.
    /// </summary>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="options">Optional: Options used to control feature flag evaluation.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>
    /// A dictionary containing all the feature flags. The key is the feature flag key and the value is the feature flag.
    /// </returns>
    Task<IReadOnlyDictionary<string, FeatureFlag>> GetAllFeatureFlagsAsync(
        string distinctId,
        AllFeatureFlagsOptions? options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Flushes the event queue and sends all queued events to PostHog.
    /// </summary>
    /// <returns>A <see cref="Task"/>.</returns>
    Task FlushAsync();

    /// <summary>
    /// The version of this library.
    /// </summary>
    string Version { get; }
}