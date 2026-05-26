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