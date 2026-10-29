using System.Text.Json.Serialization;
using PostHog.Json;
// System.Text.Json 9+ ships its own JsonStringEnumMemberNameAttribute. Bind to PostHog's,
// which JsonStringEnumMemberNameJsonConverter reads and which is shipped public API.
using JsonStringEnumMemberName = PostHog.Json.JsonStringEnumMemberNameAttribute;

namespace PostHog.Features;

/// <summary>
/// Why local evaluation could not resolve a flag that has a loaded definition. See
/// <see cref="FeatureFlagEvaluations.UnresolvedFlags"/>.
/// </summary>
/// <remarks>
/// When more than one cause applies to a flag, the first reason in declaration order is reported.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumMemberNameJsonConverter<UnresolvedFlagReason>))]
public enum UnresolvedFlagReason
{
    /// <summary>
    /// The flag has experience continuity enabled. Local evaluation never resolves it; to fix it, change the flag.
    /// </summary>
    [JsonStringEnumMemberName("experience_continuity")]
    ExperienceContinuity,

    /// <summary>
    /// The flag uses something the local evaluator does not support, such as a static cohort, an unknown
    /// operator, a malformed value, a dependency with no loaded definition, or a circular dependency. To fix it,
    /// change the flag or upgrade the SDK.
    /// </summary>
    [JsonStringEnumMemberName("unsupported_definition")]
    UnsupportedDefinition,

    /// <summary>
    /// A flag this flag depends on has a loaded definition but was unresolved.
    /// </summary>
    [JsonStringEnumMemberName("unresolved_dependency")]
    UnresolvedDependency,

    /// <summary>
    /// The call did not pass a property the flag needs, or passed it in a form the evaluator cannot use. To fix
    /// it, pass the property.
    /// </summary>
    [JsonStringEnumMemberName("missing_context")]
    MissingContext
}

/// <summary>
/// Extensions for <see cref="UnresolvedFlagReason"/>.
/// </summary>
public static class UnresolvedFlagReasonExtensions
{
    /// <summary>
    /// Returns the stable string for the reason that every PostHog SDK uses, for example
    /// <c>experience_continuity</c>.
    /// </summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The stable string for <paramref name="reason"/>.</returns>
    public static string ToValueString(this UnresolvedFlagReason reason) => reason switch
    {
        UnresolvedFlagReason.ExperienceContinuity => "experience_continuity",
        UnresolvedFlagReason.UnsupportedDefinition => "unsupported_definition",
        UnresolvedFlagReason.UnresolvedDependency => "unresolved_dependency",
        UnresolvedFlagReason.MissingContext => "missing_context",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };

    // The enum is declared in precedence order, so the lower value wins.
    internal static UnresolvedFlagReason Pick(UnresolvedFlagReason? current, UnresolvedFlagReason next)
        => current is { } existing && existing < next ? existing : next;
}
