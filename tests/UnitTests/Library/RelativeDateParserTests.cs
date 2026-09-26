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
    public void CanCompareSpecifiedDateWithRelativeDate(string relativeDateString, string nowDate, string expectedBoundary)
    {
        var now = DateTimeOffset.Parse(nowDate, CultureInfo.InvariantCulture);
        var boundary = DateTimeOffset.Parse(expectedBoundary, CultureInfo.InvariantCulture);

        var relativeDate = RelativeDate.Parse(relativeDateString);

        Assert.NotNull(relativeDate);
        Assert.True(relativeDate.IsDateBefore(boundary.AddTicks(-1), now));
        Assert.False(relativeDate.IsDateBefore(boundary, now));
        Assert.False(relativeDate.IsDateBefore(boundary.AddTicks(1), now));
        Assert.True(relativeDate.IsDateBefore(boundary.AddTicks(-1).UtcDateTime, now));
        Assert.False(relativeDate.IsDateBefore(boundary.UtcDateTime, now));
        Assert.False(relativeDate.IsDateBefore(boundary.AddTicks(1).UtcDateTime, now));
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
    public void ReturnsNullForBadFormats(string relativeDateString)
    {
        Assert.Null(RelativeDate.Parse(relativeDateString));
    }
}