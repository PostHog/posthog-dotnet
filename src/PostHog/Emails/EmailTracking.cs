using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostHog.Emails;

/// <summary>
/// What PostHog tracks for an email your code rendered: opens, clicks and engagement events.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="All"/> to turn on opens, clicks and engagement events, or <see cref="None"/> to turn them all off:
/// the email then has no open pixel and no rewritten links, and PostHog captures no engagement events for it. A
/// <see cref="bool"/> converts to one of these. To set each kind on its own, create an instance and set
/// <see cref="Opens"/>, <see cref="Clicks"/> or <see cref="Engagement"/>. A kind left unset takes its default.
/// </para>
/// <para>
/// Clicks are off by default because PostHog can't tell whether an email's links carry login or reset tokens: turn
/// them on only when the links are safe to rewrite. Click tracking needs open tracking.
/// </para>
/// </remarks>
[JsonConverter(typeof(EmailTrackingJsonConverter))]
public sealed class EmailTracking
{
    /// <summary>
    /// Creates tracking that sets each kind on its own. A kind left unset takes its default.
    /// </summary>
    public EmailTracking()
    {
    }

    EmailTracking(bool all) => AllKinds = all;

    /// <summary>
    /// Turns on opens, clicks and engagement events.
    /// </summary>
    public static EmailTracking All { get; } = new(true);

    /// <summary>
    /// Turns off opens, clicks and engagement events, including any kind PostHog adds later.
    /// </summary>
    public static EmailTracking None { get; } = new(false);

    /// <summary>
    /// Whether the email carries PostHog's open pixel, so PostHog counts its opens. (Default: <c>true</c>)
    /// </summary>
    public bool? Opens { get; init; }

    /// <summary>
    /// Whether PostHog rewrites the email's links to count clicks. Links you opt out one by one stay as they are.
    /// (Default: <c>false</c>)
    /// </summary>
    public bool? Clicks { get; init; }

    /// <summary>
    /// Whether PostHog captures the email's engagement events (sent, delivered, opened, clicked, bounced) as events
    /// of the recipient. A project whose admin turned engagement capture off captures nothing, whatever the send
    /// says. (Default: <c>true</c>)
    /// </summary>
    public bool? Engagement { get; init; }

    internal bool? AllKinds { get; }

    /// <summary>
    /// Converts <c>true</c> to <see cref="All"/> and <c>false</c> to <see cref="None"/>.
    /// </summary>
    /// <param name="all">Whether to turn on every kind of tracking.</param>
    public static implicit operator EmailTracking(bool all) => FromBoolean(all);

    /// <summary>
    /// Returns <see cref="All"/> for <c>true</c> and <see cref="None"/> for <c>false</c>.
    /// </summary>
    /// <param name="all">Whether to turn on every kind of tracking.</param>
    /// <returns><see cref="All"/> or <see cref="None"/>.</returns>
    public static EmailTracking FromBoolean(bool all) => all ? All : None;
}

internal sealed class EmailTrackingJsonConverter : JsonConverter<EmailTracking>
{
    public override EmailTracking Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("The client only sends EmailTracking.");

    public override void Write(Utf8JsonWriter writer, EmailTracking value, JsonSerializerOptions options)
    {
        if (value.AllKinds is { } all)
        {
            writer.WriteBooleanValue(all);
            return;
        }

        writer.WriteStartObject();
        WriteKind(writer, "opens", value.Opens);
        WriteKind(writer, "clicks", value.Clicks);
        WriteKind(writer, "engagement", value.Engagement);
        writer.WriteEndObject();
    }

    static void WriteKind(Utf8JsonWriter writer, string name, bool? on)
    {
        if (on is { } value)
        {
            writer.WriteBoolean(name, value);
        }
    }
}
