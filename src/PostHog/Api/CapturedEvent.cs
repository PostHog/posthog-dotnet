using System.Text.Json.Serialization;
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
    public CapturedEvent(
        string eventName,
        string distinctId,
        bool disableGeoIp,
        Dictionary<string, object>? properties,
        DateTimeOffset timestamp)
    {
        EventName = eventName;
        Timestamp = timestamp;

        Properties = properties ?? new Dictionary<string, object>();

        // Every event has to have these properties.
        Properties["distinct_id"] = distinctId; // See `get_distinct_id` in PostHog/posthog api/capture.py line 321
        Properties["$lib"] = PostHogApiClient.LibraryName;
        Properties["$lib_version"] = VersionConstants.Version;
        if (disableGeoIp)
        {
            Properties["$geoip_disable"] = true;
        }
    }

    /// <summary>
    /// The event name.
    /// </summary>
    [JsonPropertyName("event")]
    public string EventName { get; }

    /// <summary>
    /// The properties to send with the event.
    /// </summary>
    public Dictionary<string, object> Properties { get; }

    /// <summary>
    /// The timestamp of the event.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}