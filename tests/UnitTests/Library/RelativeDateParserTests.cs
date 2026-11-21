using System.Globalization;
using PostHog.Library;

namespace RelativeDateParserTests;

public class TheParseMethod
{
    [Theory]
    [InlineData("-000h", "2024-01-22T22:15:50Z", "2024-01-22T22:15:50Z")]
    [InlineData("-30h", "2024-01-22T22:15:50Z", "2024-01-21T16:15:50Z")]
    [InlineData("-24d", "2024-01-22T22:15:50Z", "2023-12-29T22:15:50Z")]
    [InlineData("-2w", "2024-01-22T22:15:50Z", "2024-01-08T22:15:50Z")]
    [InlineData("-1m", "2024-01-22T22:15:50Z", "2023-12-22T22:15:50Z")]
    [InlineData("-1y", "2024-01-22T22:15:50Z", "2023-01-22T22:15:50Z")]
    // The minus sign is optional: both forms look back.
    [InlineData("1d", "2025-01-08T00:00:00Z", "2025-01-07T00:00:00Z")]
    [InlineData("0h", "2025-01-08T00:00:00Z", "2025-01-08T00:00:00Z")]
    [InlineData("3w", "2025-01-08T00:00:00Z", "2024-12-18T00:00:00Z")]
    // Months and years are subtracted one at a time, clamping the day after every step.
    [InlineData("2m", "2025-03-31T12:00:00Z", "2025-01-28T12:00:00Z")]
    [InlineData("-13m", "2025-03-31T12:00:00Z", "2024-02-28T12:00:00Z")]
    [InlineData("-1m", "2024-03-31T12:00:00Z", "2024-02-29T12:00:00Z")]
    [InlineData("-9999m", "2025-03-31T12:00:00Z", "1191-12-28T12:00:00Z")]
    [InlineData("-4y", "2024-02-29T12:00:00Z", "2020-02-28T12:00:00Z")]
    [InlineData("-1y", "2024-02-29T12:00:00Z", "2023-02-28T12:00:00Z")]
    [InlineData("-2024y", "2025-01-08T00:00:00Z", "0001-01-08T00:00:00Z")]
    [InlineData("0y", "2024-02-29T12:00:00Z", "2024-02-29T12:00:00Z")]
    public void ResolvesRelativeDateFromNow(string relativeDateString, string nowDate, string expectedBoundary)
    {
        var now = DateTimeOffset.Parse(nowDate, CultureInfo.InvariantCulture);
        var boundary = DateTimeOffset.Parse(expectedBoundary, CultureInfo.InvariantCulture);

        var relativeDate = RelativeDate.Parse(relativeDateString);

        Assert.NotNull(relativeDate);
        Assert.Equal(boundary, relativeDate.GetDate(now));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1x")]
    [InlineData("1.2y")]
    [InlineData("1z")]
    [InlineData("1s")]
    [InlineData("-u10_001h")]
    [InlineData("10_001h")]
    [InlineData("-10000h")]
    [InlineData("-999999999999999999999h")]
    [InlineData("bazinga")]
    [InlineData("")]
    [InlineData("--1d")]
    [InlineData("+1d")]
    [InlineData(" 1d")]
    public void ReturnsNullForBadFormats(string relativeDateString)
    {
        Assert.Null(RelativeDate.Parse(relativeDateString));
    }

    [Theory]
    [InlineData("-2025y", "2025-01-08T00:00:00Z")]
    [InlineData("9999y", "2025-01-08T00:00:00Z")]
    [InlineData("-9999m", "0500-01-08T00:00:00Z")]
    public void HasNoDateBeforeTheFirstYear(string relativeDateString, string nowDate)
    {
        var now = DateTimeOffset.Parse(nowDate, CultureInfo.InvariantCulture);

        var relativeDate = RelativeDate.Parse(relativeDateString);

        Assert.NotNull(relativeDate);
        Assert.Null(relativeDate.GetDate(now));
    }
}