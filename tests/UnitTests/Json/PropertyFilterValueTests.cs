using System.Text.Json;
using PostHog.Json;

namespace PropertyFilterValueTests;

public class TheIsExactMatchMethod
{
    [Theory]
    [InlineData("scooby", "\"scooby\"", true)]
    [InlineData("SCOOBY", "\"scooby\"", true)]
    [InlineData("ScOoBy", "\"sCoObY\"", true)]
    [InlineData("", "\"shaggy\"", false)]
    [InlineData(null, "\"shaggy\"", false)]
    [InlineData("scooby", "\"shaggy\"", false)]
    [InlineData("SCOOBY", "\"shaggy\"", false)]
    [InlineData("ScOoBy", "\"shaggy\"", false)]
    [InlineData("scooby", """["SCOOBY", "SHAGGY"]""", true)]
    [InlineData("", """["SCOOBY", "SHAGGY"]""", false)]
    [InlineData(null, """["SCOOBY", "SHAGGY"]""", false)]
    [InlineData("scooby", """["SHAGGY", "FRED"]""", false)]
    [InlineData(42, """["1", "23", "42"]""", true)]
    [InlineData(45, """["1", "23", "42"]""", false)]
    [InlineData("42", """["1", "23", "42"]""", true)]
    [InlineData("45", """["1", "23", "42"]""", false)]
    [InlineData("42.5", """["1", "23", "42.5"]""", true)]
    [InlineData(3.14, """["1", "3.14", "42"]""", true)]
    [InlineData(3.14, """["1", "1.618", "42"]""", false)]
    [InlineData(true, "true", true)]
    [InlineData(false, "false", true)]
    [InlineData(true, "false", false)]
    [InlineData(false, "true", false)]
    [InlineData("true", "true", true)]
    [InlineData("false", "false", true)]
    [InlineData("true", "false", false)]
    [InlineData("false", "true", false)]
    public void ReturnsTrueWhenPropertyValueMatchesString(object? overrideValue, string jsonValue, bool expected)
    {
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse(jsonValue).RootElement);

        Assert.NotNull(filterPropertyValue);
        Assert.Equal(expected, filterPropertyValue.IsExactMatch(overrideValue));
    }
}

public class TheEqualsMethod
{
    [Fact]
    public void CanCompareTwoScalarValues()
    {
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse("\"21474836480\"").RootElement);
        var comparand = new PropertyFilterValue("21474836480");

        Assert.NotNull(filterPropertyValue);
        Assert.Equal("21474836480", filterPropertyValue.StringValue);
        Assert.Equal(comparand, filterPropertyValue);
    }

    [Fact]
    public void CanCompareTwoArrayValues()
    {
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse(
        """
            [
                "scooby",
                "shaggy",
                "velma",
                "daphne",
                "3.14",
                "21474836480",
                "42"
            ]
            """
        ).RootElement);
        var comparand = new PropertyFilterValue([
            "scooby",
            "shaggy",
            "velma",
            "daphne",
            "3.14",
            "21474836480",
            "42"
        ]);

        Assert.NotNull(filterPropertyValue);
        Assert.Equal(comparand, filterPropertyValue);
    }
}

public class TheCompareToMethod
{
    [Theory]
    [InlineData("\"21474836480\"", 21474836480, 0)]
    [InlineData("\"21474836480\"", "21474836480.0", 0)]
    [InlineData("\"21474836480\"", "21474836480", 0)]
    [InlineData("\"21474836479\"", 21474836480, -1)]
    [InlineData("\"21474836479\"", 21474836480.0, -1)]
    [InlineData("\"21474836479\"", "21474836480", -1)]
    [InlineData("\"21474836479\"", "21474836480.0", -1)]
    [InlineData("\"21474836481\"", 21474836480, 1)]
    [InlineData("\"21474836481\"", 21474836480.0, 1)]
    [InlineData("\"21474836481\"", "21474836480", 1)]
    [InlineData("\"21474836481\"", "21474836480.0", 1)]
    public void CanCompareTwoLongs(string jsonValue, object comparand, int expected)
    {
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse(jsonValue).RootElement);

        Assert.NotNull(filterPropertyValue);
        Assert.Equal(expected, filterPropertyValue.CompareTo(comparand));
    }

    [Theory]
    [InlineData("\"42.5\"", 42.5, 0)]
    [InlineData("\"42.5\"", "42.5", 0)]
    [InlineData("\"42.5\"", 42.4, 1)]
    [InlineData("\"42.5\"", "42.4", 1)]
    [InlineData("\"42.5\"", 42, 1)]
    [InlineData("\"42.5\"", "42", 1)]
    [InlineData("\"42.5\"", 42.6, -1)]
    [InlineData("\"42.5\"", "42.6", -1)]
    [InlineData("\"42.5\"", 43, -1)]
    [InlineData("\"42.5\"", "43", -1)]
    public void CanCompareTwoDoubles(string jsonValue, object comparand, int expected)
    {
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse(jsonValue).RootElement);

        Assert.NotNull(filterPropertyValue);
        Assert.Equal(expected, filterPropertyValue.CompareTo(comparand));
    }
}

public class TheCreateMethod
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void HandlesBooleanJsonValues(string jsonValue, bool expectedBooleanValue)
    {
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse(jsonValue).RootElement);

        Assert.NotNull(filterPropertyValue);
        Assert.Equal(expectedBooleanValue, filterPropertyValue.BooleanValue);
        Assert.Null(filterPropertyValue.StringValue);
    }
}