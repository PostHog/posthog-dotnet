using System.Text.Json;
using PostHog.Api;

namespace PostHog.Json;

/// <summary>
/// Converts <see cref="FilterType"/> values to and from their JSON wire names. A JSON <c>null</c> or empty-string
/// type reads as <see cref="FilterType.Person"/>, because older definitions send a person filter that way.
/// </summary>
internal sealed class FilterTypeJsonConverter : JsonStringEnumMemberNameJsonConverter<FilterType>
{
    public override FilterType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null
            || (reader.TokenType is JsonTokenType.String && reader.ValueTextEquals(ReadOnlySpan<byte>.Empty)))
        {
            return FilterType.Person;
        }

        return base.Read(ref reader, typeToConvert, options);
    }
}
