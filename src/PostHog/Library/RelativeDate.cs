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
    /// Gets the date this relative date points to, or <c>null</c> if its unit is not supported or it falls before the year 1.
    /// </summary>
    /// <param name="now">The current date.</param>
    public DateTimeOffset? GetDate(DateTimeOffset now)
    {
        try
        {
            return _currentDateOffsetFunc?.Invoke(now);
        }
        catch (ArgumentOutOfRangeException)
        {
            // The lookback leaves the DateTimeOffset range: the UTC instant or the local time would fall before
            // the year 1, which a positive offset reaches even when the local date doesn't.
            return null;
        }
    }

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
            "m" => now => SubtractMonths(now, number),
            "y" => now => SubtractYears(now, number),
            _ => _ => null
        };
        relativeDate = new RelativeDate(func);
        return true;

    }

    // Steps back one month at a time and clamps the day after every step, so 2m from March 31 lands on
    // January 28.
    static DateTimeOffset? SubtractMonths(DateTimeOffset now, int months)
    {
        var date = now.DateTime;
        var monthIndex = date.Year * 12 + date.Month - 1 - months;
        if (monthIndex < 12)
        {
            return null;
        }

        var day = date.Day;
        // Every month has at least 28 days, so once the day is 28 or less no step clamps it.
        for (var index = monthIndex + months - 1; day > 28 && index >= monthIndex; index--)
        {
            day = Math.Min(day, DateTime.DaysInMonth(index / 12, index % 12 + 1));
        }
        return new DateTimeOffset(new DateTime(monthIndex / 12, monthIndex % 12 + 1, day).Add(date.TimeOfDay), now.Offset);
    }

    // Steps back one year at a time. The year before a leap year never is one, so February 29 clamps to the
    // 28th on the first step and stays there.
    static DateTimeOffset? SubtractYears(DateTimeOffset now, int years)
    {
        if (now.Year - years < 1)
        {
            return null;
        }

        return years == 0 ? now : now.AddYears(-1).AddYears(1 - years);
    }

    // The minus sign is optional: 1d and -1d both look back a day, as in the other SDKs. \z rather than $, which
    // also matches before a trailing newline, and [0-9] rather than \d, which takes digits from other scripts.
    const string RelativeDatePattern = @"^-?(?<number>[0-9]+)(?<unit>[hdwmy])\z";
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