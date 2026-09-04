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
    /// <param name="timestamp">The ISO 8601 timestamp. UTC is preferred; non-UTC input is converted to the equivalent UTC instant.</param>
    public CapturedEvent(
        string eventName,
        string distinctId,
        Dictionary<string, object>? properties,
        DateTimeOffset timestamp)
        : this(
            eventName,
            distinctId,
            properties,
            timestamp,
            PostHogApiClient.LibraryName,
            VersionConstants.Version)
    {
    }

    internal CapturedEvent(
        string eventName,
        string distinctId,
        Dictionary<string, object>? properties,
        DateTimeOffset timestamp,
        string libraryName,
        string libraryVersion)
    {
        Uuid = Guid.NewGuid().ToString();
        EventName = eventName;
        DistinctId = distinctId;
        Timestamp = timestamp.ToUniversalTime();

        Properties = properties?.Copy() ?? new Dictionary<string, object>();

        // Every event has to have these properties.
        Properties[PostHogProperties.DistinctId] = distinctId; // See `get_distinct_id` in PostHog/posthog api/capture.py line 321
        Properties[PostHogProperties.Lib] = libraryName;
        Properties[PostHogProperties.LibVersion] = libraryVersion;
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