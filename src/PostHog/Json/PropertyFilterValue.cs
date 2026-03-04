using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PostHog.Api;
using PostHog.Library;
using static PostHog.Library.Ensure;
using static PostHog.Library.SemanticVersion;

namespace PostHog.Json;

/// <summary>
/// Represents a filter property value (<see cref="PropertyFilter"/>). This is the value that is used to compare against
/// the value of a property in a user or group, often called the "override value".
/// </summary>
/// <remarks>
/// The supported types are limited to the types we store in filter property values.
/// </remarks>
[JsonConverter(typeof(PropertyFilterValueJsonConverter))]
public class PropertyFilterValue
{
    /// <summary>
    /// If this value is a string, this property will be set.
    /// </summary>
    public string? StringValue { get; }

    /// <summary>
    /// If this value is an array of strings, this property will be set.
    /// </summary>
    public IReadOnlyList<string>? ListOfStrings { get; }

    /// <summary>
    /// If this value is a boolean, this property will be set.
    /// </summary>
    public bool? BooleanValue { get; }

    /// <summary>
    /// Creates a new instance of <see cref="PropertyFilterValue"/> from the specified <paramref name="jsonElement"/>.
    /// </summary>
    /// <remarks>
    /// When creating a feature flag condition on PostHog, even if you specify a value for a numeric type,
    /// the value gets sent as a string.
    /// </remarks>
    /// <param name="jsonElement">A JsonElement</param>
    /// <returns>A <see cref="PropertyFilterValue"/>.</returns>
    public static PropertyFilterValue? Create(JsonElement jsonElement) =>
        jsonElement.ValueKind switch
        {
            JsonValueKind.String => jsonElement.GetString() is { } stringValue ? new PropertyFilterValue(stringValue) : null,
            JsonValueKind.Array when TryParseStringArray(jsonElement, out var stringArrayValue)
                => new PropertyFilterValue(stringArrayValue),
            JsonValueKind.Number => new PropertyFilterValue(jsonElement.GetInt64()),
            JsonValueKind.True => new PropertyFilterValue(true),
            JsonValueKind.False => new PropertyFilterValue(false),
            JsonValueKind.Undefined => null,
            JsonValueKind.Null => null,
            _ => throw new ArgumentException($"JsonValueKind: {jsonElement.ValueKind} is not supported for filter property values.", nameof(jsonElement))
        };

    public PropertyFilterValue(IReadOnlyList<string> listOfStrings)
    {
        ListOfStrings = listOfStrings;
    }

    public PropertyFilterValue(long cohortId)
    {
        CohortId = cohortId;
    }

    /// <summary>
    /// The cohort ID for this property filter.
    /// </summary>
    /// <remarks>As far as I can tell, this is the only place we have a numeric.</remarks>
    public long? CohortId { get; }

    public PropertyFilterValue(string stringValue)
    {
        StringValue = stringValue;
    }

    public PropertyFilterValue(bool booleanValue)
    {
        BooleanValue = booleanValue;
    }

    /// <summary>
    /// Does a regular expression match on this instance with the specified <paramref name="input"/> instance.
    /// </summary>
    /// <param name="input">The value to search with a regex. For non-strings, we'll call ToString and run the regex.</param>
    /// <returns><c>true</c>If the current value is a valid regex and it matches the other value.</returns>
    public bool IsRegexMatch(object? input)
    {
        if (input is null)
        {
            return false;
        }

        if (StringValue is null || !RegexHelpers.TryValidateRegex(StringValue, out var regex, RegexOptions.None))
        {
            return false;
        }

        return regex.IsMatch(NotNull(input.ToString()));
    }

    /// <summary>
    /// Returns a value indicating whether this instance is contained by the specified <paramref name="other"/> instance.
    /// </summary>
    /// <param name="other">The other value to compare to this one.</param>
    /// <param name="stringComparison">The type of comparison if these are strings.</param>
    /// <returns><c>true</c> if this instance contains the other.</returns>
    public bool IsContainedBy(object? other, StringComparison stringComparison) =>
        other?.ToString() is { } comparandString
        && StringValue is not null
        && comparandString.Contains(StringValue, stringComparison);

    /// <summary>
    /// Determines whether the specified <paramref name="overrideValue"/> is an "exact" match for this instance.
    /// If this instance is an array, then it's checking to see if the value is in the array.
    /// </summary>
    /// <param name="overrideValue">The override value.</param>
    /// <returns><c>true</c> if the override value is an "exact" match for this value.</returns>
    public bool IsExactMatch(object? overrideValue)
    {
        return this switch
        {
            { ListOfStrings: { } listOfStrings } => overrideValue?.ToString() is { } stringValue
                && listOfStrings.Contains(stringValue, StringComparer.OrdinalIgnoreCase),
            { StringValue: { } stringValue } => stringValue.Equals(overrideValue?.ToString(), StringComparison.OrdinalIgnoreCase),
            { BooleanValue: { } booleanValue } => overrideValue switch
            {
                bool boolOverride => booleanValue == boolOverride,
                string stringOverride => booleanValue.ToString().Equals(stringOverride, StringComparison.OrdinalIgnoreCase),
                _ => false
            },
            _ => false
        };
    }

    /// <summary>
    /// Compares this instance with the specified <paramref name="overrideValue"/> instance and indicates whether this
    /// instance precedes, follows, or appears in the same position in the sort order as the specified instance.
    /// Less than zero: This instance precedes <paramref name="overrideValue"/> in the sort order.
    /// Zero: This instance appears in the same position in the sort order as <paramref name="overrideValue"/>.
    /// Greater than zero: This instance follows <paramref name="overrideValue"/> in the sort order or other is null.
    /// </summary>
    /// <remarks>
    /// For string values, does a case-insensitive comparison.
    /// </remarks>
    /// <param name="overrideValue">The <see cref="PropertyFilterValue"/> to compare with.</param>
    /// <returns>
    /// A value that indicates the relative order of the objects being compared. The return value has these meanings:
    /// 0: This instance and <paramref name="overrideValue"/> are equal.
    /// -1: This instance precedes <paramref name="overrideValue"/> in the sort order.
    /// 1: This instance follows <paramref name="overrideValue"/> in the sort order.
    /// </returns>
    public int CompareTo(object? overrideValue)
    {
        if (ReferenceEquals(overrideValue, null))
        {
            return 1;
        }

        return overrideValue switch
        {
            _ when TryCompareNumbers(overrideValue, out var result) => result.Value,
            _ when BooleanValue.HasValue => CompareBooleanValue(overrideValue),
            _ => string.Compare(StringValue, overrideValue.ToString(), StringComparison.OrdinalIgnoreCase)
        };
    }

    bool TryCompareNumbers(object overrideValue, [NotNullWhen(returnValue: true)] out int? result)
    {
        if (!double.TryParse(StringValue, out var doubleValue))
        {
            result = null;
            return false;
        }

        result = overrideValue switch
        {
            double overrideDouble => doubleValue.CompareTo(overrideDouble),
            long overrideLong => doubleValue.CompareTo(overrideLong),
            int overrideInt => doubleValue.CompareTo(overrideInt),
            string overrideString when double.TryParse(overrideString, out var doubleOverrideValue) => doubleValue.CompareTo(doubleOverrideValue),
            _ => null
        };
        return result is not null;
    }

    int CompareBooleanValue(object overrideValue)
    {
        if (!BooleanValue.HasValue)
        {
            return -1;
        }

        return overrideValue switch
        {
            bool boolOverride => BooleanValue.Value.CompareTo(boolOverride),
            string stringOverride when bool.TryParse(stringOverride, out var boolValue) => BooleanValue.Value.CompareTo(boolValue),
            _ => string.Compare(BooleanValue.Value.ToString(), overrideValue.ToString(), StringComparison.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Determines whether the override value represents a date before the date represented by this instance.
    /// </summary>
    /// <param name="overrideValue">The supplied override value.</param>
    /// <param name="now">The current date.</param>
    /// <returns><c>true</c> if the override date value is before the date represented by the filter value.</returns>
    /// <exception cref="InconclusiveMatchException">Thrown if the filter value can't be parsed.</exception>
    public bool IsDateBefore(object? overrideValue, DateTimeOffset now)
    {
        // Question: Should we support DateOnly and TimeOnly?
        var overrideDate = ParseDate(overrideValue);

        if (RelativeDate.TryParseRelativeDate(StringValue, out var relativeDate))
        {
            return overrideDate is DateTimeOffset overrideDateTimeOffset && relativeDate.IsDateBefore(overrideDateTimeOffset, now)
                   || (overrideDate is DateTime overrideDateTime && relativeDate.IsDateBefore(overrideDateTime, now));
        }

        return ParseDate(StringValue) switch
        {
            DateTimeOffset comparandDate => overrideDate is DateTimeOffset overrideDateTimeOffset && overrideDateTimeOffset < comparandDate,
            DateTime comparandDate => overrideDate is DateTimeOffset overrideDateTimeOffset && overrideDateTimeOffset < comparandDate,
            _ => throw new InconclusiveMatchException("The date provided is not a valid format")
        };

        object? ParseDate(object? value)
        {
            if (value is DateTime or DateTimeOffset
#if NET5_0_OR_GREATER
                or DateOnly
#endif
)
            {
                return value;
            }

            return value is string dateString
                ? DateTimeOffset.TryParse(dateString, out var dto)
                    ? dto
                    : DateTime.TryParse(dateString, out var dt)
                        ? dt
                        : throw new InconclusiveMatchException("The date provided is not a valid format")
                : throw new InconclusiveMatchException("The date provided must be a string, DateTime, or DateTimeOffset, object");
        }
    }

    public static bool operator >(PropertyFilterValue left, object? right) => NotNull(left).CompareTo(right) > 0;
    public static bool operator <(PropertyFilterValue? left, object? right) => NotNull(left).CompareTo(right) < 0;
    public static bool operator >=(PropertyFilterValue left, object? right) => NotNull(left).CompareTo(right) >= 0;
    public static bool operator <=(PropertyFilterValue left, object? right) => NotNull(left).CompareTo(right) <= 0;

    static SemanticVersion ParseOverrideSemver(object? overrideValue)
    {
        var overrideVersionString = overrideValue?.ToString();
        if (!SemanticVersion.TryParse(overrideVersionString, out var version))
        {
            throw new InconclusiveMatchException($"Cannot parse override value '{overrideVersionString}' as a semantic version");
        }
        return version.Value;
    }

    SemanticVersion ParseFilterSemver()
    {
        if (!SemanticVersion.TryParse(StringValue, out var version))
        {
            throw new InconclusiveMatchException($"Cannot parse filter value '{StringValue}' as a semantic version");
        }
        return version.Value;
    }

    /// <summary>
    /// Compares the override value as a semantic version against this filter value.
    /// </summary>
    /// <param name="overrideValue">The version value from person/group properties.</param>
    /// <returns>A comparison result: negative if override &lt; filter, zero if equal, positive if override &gt; filter.</returns>
    /// <exception cref="InconclusiveMatchException">Thrown if either value cannot be parsed as a valid semver.</exception>
    public int CompareSemver(object? overrideValue)
    {
        var overrideVersion = ParseOverrideSemver(overrideValue);
        var filterVersion = ParseFilterSemver();
        return overrideVersion.CompareTo(filterVersion);
    }

    /// <summary>
    /// Checks if the override value is within the tilde range specified by this filter value.
    /// ~X.Y.Z means >=X.Y.Z and &lt;X.Y+1.0
    /// </summary>
    /// <param name="overrideValue">The version value from person/group properties.</param>
    /// <returns><c>true</c> if the override version is within the tilde range.</returns>
    /// <exception cref="InconclusiveMatchException">Thrown if either value cannot be parsed as a valid semver.</exception>
    public bool IsSemverTildeMatch(object? overrideValue)
    {
        var overrideVersion = ParseOverrideSemver(overrideValue);
        var filterVersion = ParseFilterSemver();
        var (lower, upper) = filterVersion.GetTildeBounds();
        return overrideVersion.IsInRange(lower, upper);
    }

    /// <summary>
    /// Checks if the override value is within the caret range specified by this filter value.
    /// ^X.Y.Z is compatible-with per semver spec:
    /// - ^1.2.3 means >=1.2.3 &lt;2.0.0 (major > 0)
    /// - ^0.2.3 means >=0.2.3 &lt;0.3.0 (major = 0, minor > 0)
    /// - ^0.0.3 means >=0.0.3 &lt;0.0.4 (major = 0, minor = 0)
    /// </summary>
    /// <param name="overrideValue">The version value from person/group properties.</param>
    /// <returns><c>true</c> if the override version is within the caret range.</returns>
    /// <exception cref="InconclusiveMatchException">Thrown if either value cannot be parsed as a valid semver.</exception>
    public bool IsSemverCaretMatch(object? overrideValue)
    {
        var overrideVersion = ParseOverrideSemver(overrideValue);
        var filterVersion = ParseFilterSemver();
        var (lower, upper) = filterVersion.GetCaretBounds();
        return overrideVersion.IsInRange(lower, upper);
    }

    /// <summary>
    /// Checks if the override value matches the wildcard pattern specified by this filter value.
    /// "X.*" or "X" means >=X.0.0 &lt;X+1.0.0
    /// "X.Y.*" means >=X.Y.0 &lt;X.Y+1.0
    /// </summary>
    /// <param name="overrideValue">The version value from person/group properties.</param>
    /// <returns><c>true</c> if the override version matches the wildcard pattern.</returns>
    /// <exception cref="InconclusiveMatchException">Thrown if either value cannot be parsed.</exception>
    public bool IsSemverWildcardMatch(object? overrideValue)
    {
        var overrideVersion = ParseOverrideSemver(overrideValue);

        if (!TryParseWildcard(StringValue, out var lower, out var upper))
        {
            throw new InconclusiveMatchException($"Cannot parse filter value '{StringValue}' as a wildcard pattern");
        }

        return overrideVersion.IsInRange(lower.Value, upper.Value);
    }

    public override string ToString()
    {
        return this switch
        {
            { StringValue: { } stringValue } => stringValue,
            { CohortId: { } cohortId } => cohortId.ToString(CultureInfo.InvariantCulture),
            { ListOfStrings: { } listOfStrings } => $"[{string.Join(", ", listOfStrings)}]",
#pragma warning disable CA1308
            { BooleanValue: { } booleanValue } => booleanValue.ToString().ToLowerInvariant(),
#pragma warning restore CA1308
            _ => string.Empty
        };
    }

    public override bool Equals(object? obj) =>
        obj is PropertyFilterValue other
        && Equals(other);

    public override int GetHashCode() => HashCode.Combine(StringValue, ListOfStrings, BooleanValue);

    /// <summary>
    /// Determines if this instance is equal to the specified <paramref name="other"/> <see cref="PropertyFilterValue"/>
    /// instance. This should not be used when evaluating filter property conditions.
    /// </summary>
    /// <param name="other">The <see cref="PropertyFilterValue"/> to compare to.</param>
    /// <returns><c>true</c> if these represent the same filter property value.</returns>
    public bool Equals(PropertyFilterValue? other)
    {
        if (ReferenceEquals(other, null))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return ListOfStrings.ListsAreEqual(other.ListOfStrings)
               && StringValue == other.StringValue
               && CohortId == other.CohortId
               && BooleanValue == other.BooleanValue;
    }

    static bool TryParseStringArray(
        JsonElement jsonElement,
        [NotNullWhen(returnValue: true)] out IReadOnlyList<string>? value)
    {
        List<string> values = [];
        foreach (var element in jsonElement.EnumerateArray())
        {
            if (element.ValueKind is not JsonValueKind.String)
            {
                value = null;
                return false;
            }
            if (element.GetString() is { } stringValue)
            {
                values.Add(stringValue);
            }
        }

        value = values.ToReadOnlyList();
        return true;
    }
}