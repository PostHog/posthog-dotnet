using PostHog.Library;

namespace UuidV7Tests;

public class TheNewFallbackStringMethod
{
    [Fact]
    public void GeneratesMonotonicUuidStringsWithinSameMillisecond()
    {
        const long sameMillisecond = 1_735_689_600_000;

        var first = UuidV7.NewFallbackString(() => sameMillisecond);
        var second = UuidV7.NewFallbackString(() => sameMillisecond);

        Assert.True(Guid.TryParse(first, out _));
        Assert.True(Guid.TryParse(second, out _));
        Assert.Equal('7', first[14]);
        Assert.Equal('7', second[14]);
        Assert.True(string.CompareOrdinal(first, second) < 0);
    }
}
