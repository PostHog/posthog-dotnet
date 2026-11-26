using System.Text.Json;

namespace PostHog.Api;

/// <summary>
/// A filter whose type this SDK can't evaluate locally, such as <c>person_metadata</c> or a type added after the SDK
/// shipped. Local evaluation treats it as inconclusive.
/// </summary>
/// <param name="Definition">The filter as received, kept so it serializes back unchanged.</param>
internal sealed record UnsupportedFilter(JsonElement Definition) : Filter
{
    // Compare the JSON text, so a reloaded definition equals the one it replaces.
    public bool Equals(UnsupportedFilter? other)
        => other is not null && Definition.GetRawText() == other.Definition.GetRawText();

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Definition.GetRawText());
}
