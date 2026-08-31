using System.Text.Json;
using PostHog.Json;
using UnitTests.Library;

namespace PropertyFilterValueTests;

public class TheIsExactMatchMethod
{
    [Theory]
    [InlineData("scooby", "\"scooby\"", true)]
    [InlineData("SCOOBY", "\"scooby\"", true)]
    [InlineData("ScOoBy", "\"sCoObY\"", true)]
    [InlineData("ä", "\"Ä\"", true)]
    [InlineData("i\u0307", "\"\\u0130\"", true)]
    [InlineData("ς", "\"Σ\"", false)]
    [InlineData("ος", "\"ΟΣ\"", true)]
    [InlineData("οσ", "\"ΟΣ\"", false)]
    [InlineData("οδος", "\"ΟΔΟΣ\"", true)]
    [InlineData("οδοσ", "\"ΟΔΟΣ\"", false)]
    [InlineData("παραγγελιες", "\"ΠΑΡΑΓΓΕΛΙΕΣ\"", true)]
    [InlineData("παραγγελιεσ", "\"ΠΑΡΑΓΓΕΛΙΕΣ\"", false)]
    [InlineData("παραγγελιες", "[\"ΠΑΡΑΓΓΕΛΙΕΣ\"]", true)]
    [InlineData("παραγγελιεσ", "[\"ΠΑΡΑΓΓΕΛΙΕΣ\"]", false)]
    [InlineData("a\u0301ς", "\"A\\u0301Σ\"", true)]
    [InlineData("aς\u0301", "\"AΣ\\u0301\"", true)]
    [InlineData("aσ\u0301b", "\"AΣ\\u0301B\"", true)]
    [InlineData("a.ς", "\"A.Σ\"", true)]
    [InlineData("a σ", "\"A Σ\"", true)]
    [InlineData("ss", "\"ß\"", false)]
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
    [InlineData(0, "[0]", true)]
    [InlineData("0", "[0]", true)]
    [InlineData(3.14, "[1, 3.14, 42]", true)]
    [InlineData(1.0, "[1.0]", true)]
    [InlineData(1, "[\"1.0\"]", false)]
    [InlineData(1000, "[1e3]", true)]
    [InlineData(0.0000001, "[1e-7]", true)]
    [InlineData(1e-100, "[1e-100]", true)]
    [InlineData(0, "[1e-100]", false)]
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

    [Fact]
    public void MatchesBackendBooleanArrayPrecedence()
    {
        var cases = new (string FilterJson, object? OverrideValue, bool Expected)[]
        {
            ("false", "banana", true),
            ("\"false\"", 0, true),
            ("[\"false\"]", null, true),
            ("[\"true\",\"false\"]", "true", false),
            ("[\"true\",\"false\"]", "pro", true),
            ("[]", true, true),
            ("[]", "true", true),
            ("[]", Array.Empty<object>(), true),
            ("[]", new object[] { true }, true),
            ("[]", false, false),
            ("[]", "banana", false),
            ("[\"FREE\",\"PRO\"]", "pro", true),
            ("\"falſe\"", 0, false)
        };

        foreach (var (filterJson, overrideValue, expected) in cases)
        {
            var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse(filterJson).RootElement);

            Assert.NotNull(filterPropertyValue);
            Assert.Equal(expected, filterPropertyValue.IsExactMatch(overrideValue));
        }
    }

    [Fact]
    public void StringifiesJsonValuesLikeTheFlagsService()
    {
        var cases = new (string FilterJson, object OverrideValue, bool Expected)[]
        {
            ("\"[1,2]\"", new object[] { 1, 2 }, true),
            ("\"{\\\"a\\\":2,\\\"b\\\":1}\"", new Dictionary<string, object?> { ["b"] = 1, ["a"] = 2 }, true),
            ("\"{\\\"a\\\":{\\\"c\\\":3,\\\"d\\\":4},\\\"z\\\":0}\"", new Dictionary<string, object?> { ["z"] = 0, ["a"] = new Dictionary<string, object?> { ["d"] = 4, ["c"] = 3 } }, true),
            ("\"{\\\"\\\":1,\\\"𐀀\\\":2}\"", new Dictionary<string, object?> { ["𐀀"] = 2, [""] = 1 }, true),
            ("\"{\\\"a\\\":2,\\\"b\\\":1}\"", JsonDocument.Parse("{\"b\":1,\"a\":2}").RootElement, true),
            ("\"1.0\"", JsonDocument.Parse("1.0").RootElement, true),
            ("\"1e-7\"", 1e-7, true),
            ("\"1000000000000000.0\"", 1e15, true),
            ("\"1e+16\"", 1e16, true),
            ("\"0.00001\"", 1e-5, true),
            ("\"0.000099\"", 9.9e-5, true),
            ("\"-0.0\"", -0.0, true)
        };

        foreach (var (filterJson, overrideValue, expected) in cases)
        {
            var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse(filterJson).RootElement);

            Assert.NotNull(filterPropertyValue);
            Assert.Equal(expected, filterPropertyValue.IsExactMatch(overrideValue));
        }
    }

    [Fact]
    public void QuotesNestedJsonStringElements()
    {
        using var stringDocument = JsonDocument.Parse("\"x\"");
        var overrideValue = new Dictionary<string, object?>
        {
            ["document"] = stringDocument,
            ["element"] = stringDocument.RootElement
        };
        var filterPropertyValue = PropertyFilterValue.Create(
            JsonDocument.Parse("\"{\\\"document\\\":\\\"x\\\",\\\"element\\\":\\\"x\\\"}\"").RootElement);

        Assert.NotNull(filterPropertyValue);
        Assert.True(filterPropertyValue.IsExactMatch(overrideValue));
    }

    [Fact]
    public void UnrepresentableRecursiveJsonValuesDoNotCrashMatching()
    {
        var cyclicValue = new Dictionary<string, object?>();
        cyclicValue["self"] = cyclicValue;
        object deeplyNestedValue = "leaf";
        for (var depth = 0; depth < 65; depth++)
        {
            deeplyNestedValue = new object[] { deeplyNestedValue };
        }
        var recursiveArray = new object[1];
        recursiveArray[0] = recursiveArray;
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse("\"never\"").RootElement);
        var emptyObjectFilter = PropertyFilterValue.Create(JsonDocument.Parse("\"{}\"").RootElement);
        var falseFilter = PropertyFilterValue.Create(JsonDocument.Parse("false").RootElement);
        var nonStringKeyValue = new System.Collections.Hashtable { [1] = "value" };

        Assert.NotNull(filterPropertyValue);
        Assert.NotNull(emptyObjectFilter);
        Assert.NotNull(falseFilter);
        Assert.False(filterPropertyValue.IsExactMatch(cyclicValue));
        Assert.False(filterPropertyValue.IsExactMatch(deeplyNestedValue));
        Assert.False(emptyObjectFilter.IsExactMatch(nonStringKeyValue));
        Assert.True(falseFilter.IsExactMatch(recursiveArray));
    }

    [Fact]
    public void NumericArrayMatchesDecimalRegardlessOfScale()
    {
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse("[1.00]").RootElement);

        Assert.NotNull(filterPropertyValue);
        Assert.True(filterPropertyValue.IsExactMatch(1.0m));
    }

    [Fact]
    public void NumericArrayDoesNotThrowForLargeSingleOverride()
    {
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse("[0]").RootElement);

        Assert.NotNull(filterPropertyValue);
        Assert.False(filterPropertyValue.IsExactMatch(float.MaxValue));
    }

    [Theory]
    [InlineData(3.14, "\"3.14\"", true)]
    [InlineData(323.0, "\"323.0\"", true)]
    [InlineData(323.0, "\"323\"", false)]
    [InlineData(323, "\"323\"", true)]
    [InlineData(323, "\"323.0\"", false)]
    [InlineData(3.14, "\"3,14\"", false)]
    [InlineData(1.618, "\"3.14\"", false)]
    [InlineData(3.14, """["1", "3.14", "42"]""", true)]
    public void StringifiesNumbersWithInvariantCulture(object overrideValue, string jsonValue, bool expected)
    {
        using var _ = TestCulture.Use("de-DE");
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse(jsonValue).RootElement);

        Assert.NotNull(filterPropertyValue);
        Assert.Equal(expected, filterPropertyValue.IsExactMatch(overrideValue));
    }

    [Fact]
    public void StringifiesDecimalsWithInvariantCulture()
    {
        using var _ = TestCulture.Use("de-DE");
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse("\"3.14\"").RootElement);

        Assert.NotNull(filterPropertyValue);
        Assert.True(filterPropertyValue.IsExactMatch(3.14m));
    }
}

public class TheIsContainedByMethod
{
    [Theory]
    [InlineData(3.14, "\"3.14\"", true)]
    [InlineData(3.14, "\".14\"", true)]
    [InlineData(3.14, "\"3,14\"", false)]
    [InlineData(1.618, "\"3.14\"", false)]
    public void StringifiesNumbersWithInvariantCulture(object overrideValue, string jsonValue, bool expected)
    {
        using var _ = TestCulture.Use("de-DE");
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse(jsonValue).RootElement);

        Assert.NotNull(filterPropertyValue);
        Assert.Equal(expected, filterPropertyValue.IsContainedBy(overrideValue, StringComparison.OrdinalIgnoreCase));
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

    [Theory]
    [InlineData("\"hello\"")]
    [InlineData("42")]
    [InlineData("[\"item1\", \"item2\"]")]
    [InlineData("null")]
    public void NonBooleanJsonValuesHaveNullBooleanValue(string jsonValue)
    {
        var filterPropertyValue = PropertyFilterValue.Create(JsonDocument.Parse(jsonValue).RootElement);

        if (filterPropertyValue != null)
        {
            Assert.Null(filterPropertyValue.BooleanValue);
        }
    }
}