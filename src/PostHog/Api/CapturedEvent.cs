using System.Text.Json.Serialization;
using PostHog.Library;
using PostHog.Versioning;

namespace PostHog.Api;

/// <summary>
/// A captured event that will be sent as part of a batch.
/// </summary>
public class CapturedEvent
{
    /// <summary>
    /// Creates a <see cref="CapturedEvent"/>.
    /// </summary>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="distinctId">The identifier for the user.</param>
    /// <param name="properties">The properties to associate with the event.</param>
    /// <param name="timestamp">The ISO 8601 timestamp.</param>
    /// <param name="isServer">
    /// When <c>true</c> (the default), the event includes the <c>$is_server</c> property set to <c>true</c>. Set to
    /// <c>false</c> (via <see cref="PostHogOptions.IsServer"/>) when using the SDK as a client/CLI so the device OS
    /// is attributed normally; the property is then omitted.
    /// </param>
    public CapturedEvent(
        string eventName,
        string distinctId,
        Dictionary<string, object>? properties,
        DateTimeOffset timestamp,
        bool isServer = true)
    {
        Uuid = Guid.NewGuid().ToString();
        EventName = eventName;
        DistinctId = distinctId;
        Timestamp = timestamp;

        Properties = properties ?? new Dictionary<string, object>();

        // Every event has to have these properties.
        Properties[PostHogProperties.DistinctId] = distinctId; // See `get_distinct_id` in PostHog/posthog api/capture.py line 321
        Properties[PostHogProperties.Lib] = PostHogApiClient.LibraryName;
        if (isServer)
        {
            Properties[PostHogProperties.IsServer] = true;
        }
        Properties[PostHogProperties.LibVersion] = VersionConstants.Version;
        Properties[PostHogProperties.GeoIpDisable] = Properties.GetValueOrDefault(PostHogProperties.GeoIpDisable, true);
    }

    /// <summary>
    /// The unique identifier for this event. Used for deduplication.
    /// </summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; }

    /// <summary>
    /// The event name.
    /// </summary>
    [JsonPropertyName("event")]
    public string EventName { get; }

    /// <summary>
    /// The distinct ID of the user.
    /// </summary>
    [JsonPropertyName("distinct_id")]
    public string DistinctId { get; }

    /// <summary>
    /// The properties to send with the event.
    /// </summary>
    public Dictionary<string, object> Properties { get; }

    /// <summary>
    /// The timestamp of the event.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}