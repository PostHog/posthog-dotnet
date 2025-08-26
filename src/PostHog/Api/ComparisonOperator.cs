using System.Text.Json.Serialization;
using PostHog.Json;

namespace PostHog.Api;

/// <summary>
/// An enumeration representing the comparison types that can be used in a filter.
/// </summary>
[JsonConverter(typeof(JsonStringEnumMemberNameJsonConverter<ComparisonOperator>))]
public enum ComparisonOperator
{
    /// <summary>
    /// Matches if the value is in the list of filter values. Only used for cohort filters.
    /// </summary>
    [JsonStringEnumMemberName("in")]
    In,

    /// <summary>
    /// Matches if the value is an exact match to the filter value.
    /// </summary>
    [JsonStringEnumMemberName("exact")]
    Exact,

    /// <summary>
    /// Matches if the value is not an exact match to the filter value.
    /// </summary>
    [JsonStringEnumMemberName("is_not")]
    IsNot,

    /// <summary>
    /// Matches if the value is set.
    /// </summary>
    [JsonStringEnumMemberName("is_set")]
    IsSet,

    /// <summary>
    /// Matches if the value is not set.
    /// </summary>
    [JsonStringEnumMemberName("is_not_set")]
    IsNotSet,

    /// <summary>
    /// Matches if the value is greater than the filter value.
    /// </summary>
    [JsonStringEnumMemberName("gt")]
    GreaterThan,

    /// <summary>
    /// Matches if the value is less than the filter value.
    /// </summary>
    [JsonStringEnumMemberName("lt")]
    LessThan,

    /// <summary>
    /// Matches if the value is greater than or equal to the filter value.
    /// </summary>
    [JsonStringEnumMemberName("gte")]
    GreaterThanOrEquals,

    /// <summary>
    /// Matches if the value is less than or equal to the filter value.
    /// </summary>
    [JsonStringEnumMemberName("lte")]
    LessThanOrEquals,

    /// <summary>
    /// Matches if the value contains the filter value, ignoring case differences.
    /// </summary>
    [JsonStringEnumMemberName("icontains")]
    ContainsIgnoreCase,

    /// <summary>
    /// Matches if the value does not contain the filter value, ignoring case differences.
    /// </summary>
    [JsonStringEnumMemberName("not_icontains")]
    DoesNotContainIgnoreCase,

    /// <summary>
    /// Matches if the value matches the regular expression filter pattern.
    /// </summary>
    [JsonStringEnumMemberName("regex")]
    Regex,

    /// <summary>
    /// Matches if regular expression filter value does not match the value.
    /// </summary>
    [JsonStringEnumMemberName("not_regex")]
    NotRegex,

    /// <summary>
    /// Matches if the date represented by the value is before the filter value.
    /// </summary>
    [JsonStringEnumMemberName("is_date_before")]
    IsDateBefore,

    /// <summary>
    /// Matches if the date represented by the value is after the filter value.
    /// </summary>
    [JsonStringEnumMemberName("is_date_after")]
    IsDateAfter,

    /// <summary>
    /// Matches if the flag condition evaluates to the specified value.
    /// </summary>
    [JsonStringEnumMemberName("flag_evaluates_to")]
    FlagEvaluatesTo
}