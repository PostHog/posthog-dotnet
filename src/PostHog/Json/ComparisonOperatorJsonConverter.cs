using PostHog.Api;

namespace PostHog.Json;

/// <summary>
/// Converts <see cref="ComparisonOperator"/> values to and from their JSON wire names. Operator names this
/// version of the SDK doesn't recognize map to <see cref="ComparisonOperator.Unknown"/> so that a new
/// server-side operator makes only the affected flag inconclusive instead of failing deserialization of the
/// entire local evaluation response.
/// </summary>
internal sealed class ComparisonOperatorJsonConverter : JsonStringEnumMemberNameJsonConverter<ComparisonOperator>
{
    public ComparisonOperatorJsonConverter() : base(ComparisonOperator.Unknown)
    {
    }
}
