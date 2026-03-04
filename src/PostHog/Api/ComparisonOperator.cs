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
    FlagEvaluatesTo,

    /// <summary>
    /// Matches if the version exactly equals the filter version.
    /// </summary>
    [JsonStringEnumMemberName("semver_eq")]
    SemverEquals,

    /// <summary>
    /// Matches if the version does not equal the filter version.
    /// </summary>
    [JsonStringEnumMemberName("semver_neq")]
    SemverNotEquals,

    /// <summary>
    /// Matches if the version is greater than the filter version.
    /// </summary>
    [JsonStringEnumMemberName("semver_gt")]
    SemverGreaterThan,

    /// <summary>
    /// Matches if the version is greater than or equal to the filter version.
    /// </summary>
    [JsonStringEnumMemberName("semver_gte")]
    SemverGreaterThanOrEquals,

    /// <summary>
    /// Matches if the version is less than the filter version.
    /// </summary>
    [JsonStringEnumMemberName("semver_lt")]
    SemverLessThan,

    /// <summary>
    /// Matches if the version is less than or equal to the filter version.
    /// </summary>
    [JsonStringEnumMemberName("semver_lte")]
    SemverLessThanOrEquals,

    /// <summary>
    /// Matches if the version is within the tilde range (~X.Y.Z means >=X.Y.Z and &lt;X.Y+1.0).
    /// </summary>
    [JsonStringEnumMemberName("semver_tilde")]
    SemverTilde,

    /// <summary>
    /// Matches if the version is within the caret range (^X.Y.Z is compatible-with per semver spec).
    /// </summary>
    [JsonStringEnumMemberName("semver_caret")]
    SemverCaret,

    /// <summary>
    /// Matches if the version matches the wildcard pattern (e.g., "1.2.*" means >=1.2.0 and &lt;1.3.0).
    /// </summary>
    [JsonStringEnumMemberName("semver_wildcard")]
    SemverWildcard
}