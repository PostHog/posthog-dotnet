namespace PostHog.Api;

/// <summary>
/// Property names used by the SDK when sending events to PostHog.
/// </summary>
public static class PostHogProperties
{
    /// <summary>
    /// The PostHog distinct identifier property name.
    /// </summary>
    public const string DistinctId = "distinct_id";

    /// <summary>
    /// The property name for the client library identifier.
    /// </summary>
    public const string Lib = "$lib";

    /// <summary>
    /// The property name for the client library version.
    /// </summary>
    public const string LibVersion = "$lib_version";

    /// <summary>
    /// The property name that disables GeoIP enrichment for captured events.
    /// </summary>
    public const string GeoIpDisable = "$geoip_disable";

    /// <summary>
    /// The property name that controls whether PostHog should process a person profile for an event.
    /// </summary>
    public const string ProcessPersonProfile = "$process_person_profile";

    /// <summary>
    /// The property name for the PostHog session identifier.
    /// </summary>
    public const string SessionId = "$session_id";

    /// <summary>
    /// The property name for the current URL associated with an event.
    /// </summary>
    public const string CurrentUrl = "$current_url";

    /// <summary>
    /// The property name for the HTTP request method.
    /// </summary>
    public const string RequestMethod = "$request_method";

    /// <summary>
    /// The property name for the HTTP request path.
    /// </summary>
    public const string RequestPath = "$request_path";

    /// <summary>
    /// The property name for the HTTP user agent.
    /// </summary>
    public const string UserAgent = "$user_agent";

    /// <summary>
    /// The property name for the client IP address.
    /// </summary>
    public const string Ip = "$ip";

    /// <summary>
    /// The property name for the HTTP response status code.
    /// </summary>
    public const string ResponseStatusCode = "$response_status_code";

    /// <summary>
    /// The property name for the operating system.
    /// </summary>
    public const string Os = "$os";

    /// <summary>
    /// The property name for the .NET framework description.
    /// </summary>
    public const string Framework = "$framework";

    /// <summary>
    /// The property name for the process architecture.
    /// </summary>
    public const string Architecture = "$arch";
}
