using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace PostHog.Library;

/// <summary>
/// Represents a date that can be compared to other dates. This might be a relative date or a standard date.
/// </summary>
internal partial record RelativeDate
{
    static readonly Regex RelativeDateRegex = MyRegex();
    readonly Func<DateTimeOffset, DateTimeOffset?>? _currentDateOffsetFunc;

    private RelativeDate(Func<DateTimeOffset, DateTimeOffset?> currentDateOffsetFunc)
    {
        _currentDateOffsetFunc = currentDateOffsetFunc;
    }

    /// <summary>
    /// Determines whether the specified date is before the date represented by this instance.
    /// </summary>
    /// <param name="other">The date to compare to this instance.</param>
    /// <param name="now">The current date.</param>
    public bool IsDateBefore(DateTimeOffset other, DateTimeOffset now) => other < _currentDateOffsetFunc?.Invoke(now);

    /// <summary>
    /// Determines whether the specified date is before the date represented by this instance.
    /// </summary>
    /// <param name="other">The date to compare to this instance.</param>
    /// <param name="now">The current date.</param>
    public bool IsDateBefore(DateTime other, DateTimeOffset now) => other < _currentDateOffsetFunc?.Invoke(now);

    /// <summary>
    /// Tries to parse a string into a <see cref="RelativeDate"/>. If parsing fails, returns <c>null</c>.
    /// </summary>
    /// <param name="value">The relative or absolute date string.</param>
    /// <returns><c>true</c> if parsing was successful. Otherwise <c>false</c>.</returns>
    public static RelativeDate? Parse(string value)
        => TryParseRelativeDate(value, out var relativeDate) ? relativeDate : null;

    /// <summary>
    /// Tries to parse a string into a <see cref="RelativeDate"/>.
    /// </summary>
    /// <param name="value">The relative or absolute date string.</param>
    /// <param name="relativeDate">The resulting <see cref="RelativeDate"/>.</param>
    /// <returns><c>true</c> if parsing was successful. Otherwise <c>false</c>.</returns>
    public static bool TryParseRelativeDate(
        string? value,
        [NotNullWhen(returnValue: true)] out RelativeDate? relativeDate)
    {
        relativeDate = null;
        if (value is null)
        {
            return false;
        }
        var match = RelativeDateRegex.Match(value);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["number"].Value, out var number)
            || number >= 10_000) // Guard against overflow, disallow numbers greater than 10_000
        {
            return false;
        }

        var unit = match.Groups["unit"].Value;

        Func<DateTimeOffset, DateTimeOffset?> func = unit switch
        {
            "h" => now => now.AddHours(-number),
            "d" => now => now.AddDays(-number),
            "w" => now => now.AddDays(-number * 7),
            "m" => now => now.AddMonths(-number),
            "y" => now => now.AddYears(-number),
            _ => _ => null
        };
        relativeDate = new RelativeDate(func);
        return true;

    }

    const string RelativeDatePattern = @"^-(?<number>\d+)(?<unit>[hdwmy])$";
    [GeneratedRegex(pattern: RelativeDatePattern, options: RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}

#if NETSTANDARD2_0 || NETSTANDARD2_1
internal partial record RelativeDate
{
    private static partial Regex MyRegex()
    {
        return new Regex(RelativeDatePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
#endif