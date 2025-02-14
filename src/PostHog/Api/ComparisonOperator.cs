using System.Text.Json.Serialization;

namespace PostHog.Api;

/// <summary>
/// An enumeration representing the comparison types that can be used in a filter.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ComparisonOperator>))]
public enum ComparisonOperator
{
    [JsonStringEnumMemberName("in")]
    In, // Only used for cohort filters

    [JsonStringEnumMemberName("exact")]
    Exact,

    [JsonStringEnumMemberName("is_not")]
    IsNot,

    [JsonStringEnumMemberName("is_set")]
    IsSet,

    [JsonStringEnumMemberName("is_not_set")]
    IsNotSet,

    [JsonStringEnumMemberName("gt")]
    GreaterThan,

    [JsonStringEnumMemberName("lt")]
    LessThan,

    [JsonStringEnumMemberName("gte")]
    GreaterThanOrEquals,

    [JsonStringEnumMemberName("lte")]
    LessThanOrEquals,

    [JsonStringEnumMemberName("icontains")]
    ContainsIgnoreCase,

    [JsonStringEnumMemberName("not_icontains")]
    DoesNotContainsIgnoreCase,

    [JsonStringEnumMemberName("regex")]
    Regex,

    [JsonStringEnumMemberName("not_regex")]
    NotRegex,

    [JsonStringEnumMemberName("is_date_before")]
    IsDateBefore,

    [JsonStringEnumMemberName("is_date_after")]
    IsDateAfter
}