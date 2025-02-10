using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PostHog.Api;
using PostHog.Features;
using PostHog.Json;

namespace LocalEvaluatorTests;

public class TheEvaluateFeatureFlagMethod
{
    static LocalEvaluationApiResult CreateFlags(string key, IReadOnlyList<PropertyFilter> properties)
    {
        return new LocalEvaluationApiResult(
            Flags: [
                new LocalFeatureFlag(
                    Id: 42,
                    TeamId: 23,
                    Name: $"{key}-feature-flag",
                    Key: key,
                    Filters: new FeatureFlagFilters(
                        Groups: [
                            new FeatureFlagGroup(
                                Properties: properties
                            )
                        ]
                    ))
            ],
            GroupTypeMapping: new Dictionary<string, string>()
        );
    }

    [Theory]
    [InlineData("tyrion@example.com", ComparisonOperator.Exact, true)]
    [InlineData("TYRION@example.com", ComparisonOperator.Exact, true)] // Case-insensitive
    [InlineData("nobody@example.com", ComparisonOperator.Exact, false)]
    [InlineData("", ComparisonOperator.Exact, false)]
    [InlineData(null, ComparisonOperator.Exact, false)]
    [InlineData("tyrion@example.com", ComparisonOperator.IsNot, false)]
    [InlineData("TYRION@example.com", ComparisonOperator.IsNot, false)] // Case-insensitive
    [InlineData("nobody@example.com", ComparisonOperator.IsNot, true)]
    [InlineData("", ComparisonOperator.IsNot, true)]
    [InlineData(null, ComparisonOperator.IsNot, true)]
    public void HandlesExactMatchWithStringValuesArray(string? email, ComparisonOperator comparison, bool expected)
    {
        var flags = CreateFlags(
            key: "email",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "email",
                    Value: new PropertyFilterValue([
                        "tyrion@example.com",
                        "danaerys@example.com",
                        "sansa@example.com",
                        "ned@example.com"
                    ]),
                    Operator: comparison)
            ]
        );
        var properties = new Dictionary<string, object?>
        {
            ["email"] = email
        };
        var localEvaluator = new LocalEvaluator(flags);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "email",
            distinctId: "1234",
            personProperties: properties);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(42, ComparisonOperator.Exact, true)]
    [InlineData(42.5, ComparisonOperator.Exact, true)]
    [InlineData("42.5", ComparisonOperator.Exact, true)]
    [InlineData(21, ComparisonOperator.Exact, false)]
    [InlineData("42", ComparisonOperator.Exact, true)]
    [InlineData("21", ComparisonOperator.Exact, false)]
    [InlineData("", ComparisonOperator.Exact, false)]
    [InlineData(null, ComparisonOperator.Exact, false)]
    [InlineData(42, ComparisonOperator.IsNot, false)]
    [InlineData(42.5, ComparisonOperator.IsNot, false)]
    [InlineData("42.5", ComparisonOperator.IsNot, false)]
    [InlineData(21, ComparisonOperator.IsNot, true)]
    [InlineData("42", ComparisonOperator.IsNot, false)]
    [InlineData("21", ComparisonOperator.IsNot, true)]
    [InlineData("", ComparisonOperator.IsNot, true)]
    [InlineData(null, ComparisonOperator.IsNot, true)]
    public void HandlesExactMatchNumericValues(object? ageOverride, ComparisonOperator comparison, bool expected)
    {
        var flags = CreateFlags(
            key: "age",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "age",
                    Value: new PropertyFilterValue(["4", "8", "15", "16", "23", "42", "42.5" ]),
                    Operator: comparison)
            ]
        );
        var properties = new Dictionary<string, object?>
        {
            ["age"] = ageOverride
        };
        var localEvaluator = new LocalEvaluator(flags);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "age",
            distinctId: "1234",
            personProperties: properties);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("test@posthog.com", true)]
    [InlineData("", true)]
    [InlineData(null, false)]
    public void HandlesIsSet(string? email, bool expected)
    {
        var flags = CreateFlags(
            key: "email",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "email",
                    Value: new PropertyFilterValue("is_set"),
                    Operator: ComparisonOperator.IsSet)
            ]
        );
        var properties = new Dictionary<string, object?>
        {
            ["email"] = email
        };
        var localEvaluator = new LocalEvaluator(flags);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "email",
            distinctId: "1234",
            personProperties: properties);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ThrowsInconclusiveMatchExceptionWhenKeyDoesNotMatch()
    {
        var flags = CreateFlags(
            key: "email",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "email",
                    Value: new PropertyFilterValue("is_set"),
                    Operator: ComparisonOperator.IsSet)
            ]
        );
        var localEvaluator = new LocalEvaluator(flags);

        Assert.Throws<InconclusiveMatchException>(() => localEvaluator.EvaluateFeatureFlag(
            key: "email",
            distinctId: "1234",
            personProperties: new Dictionary<string, object?>
            {
                ["not-email"] = "anything"
            }));
        Assert.Throws<InconclusiveMatchException>(() => localEvaluator.EvaluateFeatureFlag(
            key: "email",
            distinctId: "1234",
            personProperties: new Dictionary<string, object?>()));
    }

    [Theory]
    [InlineData("snuffleupagus@gmail.com", ComparisonOperator.Regex, "^.*?@gmail.com$", true)]
    [InlineData("snuffleupagus@hotmail.com", ComparisonOperator.Regex, "^.*?@gmail.com$", false)]
    [InlineData("snuffleupagus@gmail.com", ComparisonOperator.NotRegex, "^.*?@gmail.com$", false)]
    [InlineData("snuffleupagus@hotmail.com", ComparisonOperator.NotRegex, "^.*?@gmail.com$", true)]
    // PostHog supports this for number types.
    [InlineData(8675309, ComparisonOperator.Regex, ".+75.+", true)]
    [InlineData(8675309, ComparisonOperator.NotRegex, ".+75.+", false)]
    [InlineData(8675309, ComparisonOperator.Regex, ".+76.+", false)]
    [InlineData(8675309, ComparisonOperator.NotRegex, ".+76.+", true)]
    public void MatchesRegexUserProperty(object overrideValue, ComparisonOperator comparison, string filterValue, bool expected)
    {
        var flags = CreateFlags(
            key: "email",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "email",
                    Value: new PropertyFilterValue(filterValue),
                    Operator: comparison)
            ]
        );
        var properties = new Dictionary<string, object?>
        {
            ["email"] = overrideValue
        };
        var localEvaluator = new LocalEvaluator(flags);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "email",
            distinctId: "distinct-id",
            personProperties: properties);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Works at PostHog", ComparisonOperator.ContainsIgnoreCase, "\"posthog\"", true)]
    [InlineData("Works at PostHog", ComparisonOperator.DoesNotContainsIgnoreCase, "\"posthog\"", false)]
    [InlineData("Works at PostHog", ComparisonOperator.DoesNotContainsIgnoreCase, "\"PostHog\"", false)]
    [InlineData("Loves puppies", ComparisonOperator.ContainsIgnoreCase, "\"cats\"", false)]
    [InlineData("Loves puppies", ComparisonOperator.DoesNotContainsIgnoreCase, "\"cats\"", true)]
    public void HandlesContainsComparisons(object overrideValue, ComparisonOperator comparison, string filterValueJson, bool expected)
    {
        var flags = CreateFlags(
            key: "bio",
            properties:
            [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "bio",
                    Value: PropertyFilterValue.Create(JsonDocument.Parse(filterValueJson).RootElement)!,
                    Operator: comparison)
            ]
        );
        var properties = new Dictionary<string, object?>
        {
            ["bio"] = overrideValue
        };
        var localEvaluator = new LocalEvaluator(flags);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "bio",
            distinctId: "distinct-id",
            personProperties: properties);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(22, ComparisonOperator.GreaterThan, "\"21\"", true)]
    [InlineData(22, ComparisonOperator.GreaterThanOrEquals, "\"21\"", true)]
    [InlineData("22", ComparisonOperator.GreaterThan, "\"21\"", true)]
    [InlineData("22", ComparisonOperator.GreaterThanOrEquals, "\"21\"", true)]
    [InlineData(20, ComparisonOperator.GreaterThan, "\"21\"", false)]
    [InlineData(20, ComparisonOperator.GreaterThanOrEquals, "\"21\"", false)]
    [InlineData("20", ComparisonOperator.GreaterThan, "\"21\"", false)]
    [InlineData("20", ComparisonOperator.GreaterThanOrEquals, "\"21\"", false)]
    [InlineData(22, ComparisonOperator.LessThan, "\"21\"", false)]
    [InlineData(22, ComparisonOperator.LessThanOrEquals, "\"21\"", false)]
    [InlineData("22", ComparisonOperator.LessThan, "\"21\"", false)]
    [InlineData("22", ComparisonOperator.LessThanOrEquals, "\"21\"", false)]
    [InlineData(20, ComparisonOperator.LessThan, "\"21\"", true)]
    [InlineData(20, ComparisonOperator.LessThanOrEquals, "\"21\"", true)]
    [InlineData("20", ComparisonOperator.LessThan, "\"21\"", true)]
    [InlineData("20", ComparisonOperator.LessThanOrEquals, "\"21\"", true)]
    [InlineData(21, ComparisonOperator.GreaterThanOrEquals, "\"21\"", true)]
    [InlineData("21", ComparisonOperator.GreaterThanOrEquals, "\"21\"", true)]
    [InlineData(21, ComparisonOperator.LessThanOrEquals, "\"21\"", true)]
    [InlineData("21", ComparisonOperator.LessThanOrEquals, "\"21\"", true)]
    public void HandlesGreaterAndLessThanComparisons(object overrideValue, ComparisonOperator comparison, string filterValueJson, bool expected)
    {
        var flags = CreateFlags(
            key: "age",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "age",
                    Value: PropertyFilterValue.Create(JsonDocument.Parse(filterValueJson).RootElement)!,
                    Operator: comparison)
            ]
        );
        var properties = new Dictionary<string, object?>
        {
            ["age"] = overrideValue
        };
        var localEvaluator = new LocalEvaluator(flags);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "age",
            distinctId: "distinct-id",
            personProperties: properties);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("2024-01-21T16:15:49Z", ComparisonOperator.IsDateBefore, "-30h", true)]
    [InlineData("2024-01-21T16:15:51Z", ComparisonOperator.IsDateBefore, "-30h", false)]
    [InlineData("2023-12-29T22:15:49Z", ComparisonOperator.IsDateBefore, "-24d", true)]
    [InlineData("2023-12-29T22:15:51Z", ComparisonOperator.IsDateBefore, "-24d", false)]
    [InlineData("2024-01-08T22:15:49Z", ComparisonOperator.IsDateBefore, "-2w", true)]
    [InlineData("2024-01-08T22:15:51Z", ComparisonOperator.IsDateBefore, "-2w", false)]
    [InlineData("2023-12-22T22:15:49Z", ComparisonOperator.IsDateBefore, "-1m", true)]
    [InlineData("2023-12-22T22:15:51Z", ComparisonOperator.IsDateBefore, "-1m", false)]
    [InlineData("2023-01-22T22:15:49Z", ComparisonOperator.IsDateBefore, "-1y", true)]
    [InlineData("2023-01-22T22:15:51Z", ComparisonOperator.IsDateBefore, "-1y", false)]
    [InlineData("2024-01-21T16:15:49Z", ComparisonOperator.IsDateAfter, "-30h", false)]
    [InlineData("2024-01-21T16:15:51Z", ComparisonOperator.IsDateAfter, "-30h", true)]
    [InlineData("2023-12-29T22:15:49Z", ComparisonOperator.IsDateAfter, "-24d", false)]
    [InlineData("2023-12-29T22:15:51Z", ComparisonOperator.IsDateAfter, "-24d", true)]
    [InlineData("2024-01-08T22:15:49Z", ComparisonOperator.IsDateAfter, "-2w", false)]
    [InlineData("2024-01-08T22:15:51Z", ComparisonOperator.IsDateAfter, "-2w", true)]
    [InlineData("2023-12-22T22:15:49Z", ComparisonOperator.IsDateAfter, "-1m", false)]
    [InlineData("2023-12-22T22:15:51Z", ComparisonOperator.IsDateAfter, "-1m", true)]
    [InlineData("2023-01-22T22:15:49Z", ComparisonOperator.IsDateAfter, "-1y", false)]
    [InlineData("2023-01-22T22:15:51Z", ComparisonOperator.IsDateAfter, "-1y", true)]
    public void CanPerformDateComparisonsAgainstDateTimeOffset(
        string joinDate,
        ComparisonOperator comparison,
        string relativeDateString,
        bool expected)
    {
        var timeProvider = new FakeTimeProvider();
        var now = DateTimeOffset.Parse("2024-01-22T22:15:50Z", CultureInfo.InvariantCulture);
        timeProvider.SetUtcNow(now);
        var flags = CreateFlags(
            key: "join_date",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "join_date",
                    Value: new PropertyFilterValue(relativeDateString),
                    Operator: comparison)
            ]
        );
        var properties = new Dictionary<string, object?>
        {
            ["join_date"] = DateTimeOffset.Parse(joinDate, CultureInfo.InvariantCulture)
        };
        var localEvaluator = new LocalEvaluator(flags, timeProvider, NullLogger<LocalEvaluator>.Instance);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "join_date",
            distinctId: "1234b",
            personProperties: properties);

        Assert.Equal(expected, result);
    }

#if !NETCOREAPP3_1
    [Theory]
    [InlineData("2024-01-21", ComparisonOperator.IsDateBefore, "-30h", false)]
    [InlineData("2023-12-29", ComparisonOperator.IsDateBefore, "-24d", false)]
    [InlineData("2024-01-08", ComparisonOperator.IsDateBefore, "-2w", false)]
    [InlineData("2023-12-22", ComparisonOperator.IsDateBefore, "-1m", false)]
    [InlineData("2023-01-22", ComparisonOperator.IsDateBefore, "-1y", false)]
    [InlineData("2024-01-21", ComparisonOperator.IsDateAfter, "-30h", true)]
    [InlineData("2023-12-29", ComparisonOperator.IsDateAfter, "-24d", true)]
    [InlineData("2024-01-08", ComparisonOperator.IsDateAfter, "-2w", true)]
    [InlineData("2023-12-22", ComparisonOperator.IsDateAfter, "-1m", true)]
    [InlineData("2023-01-22", ComparisonOperator.IsDateAfter, "-1y", true)]
    public void CanPerformDateComparisonsAgainstDateOnly(
        string joinDate,
        ComparisonOperator comparison,
        string relativeDateString,
        bool expected)
    {
        var timeProvider = new FakeTimeProvider();
        var now = DateTimeOffset.Parse("2024-01-22T22:15:50Z", CultureInfo.InvariantCulture);
        timeProvider.SetUtcNow(now);
        var flags = CreateFlags(
            key: "join_date",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "join_date",
                    Value: new PropertyFilterValue(relativeDateString),
                    Operator: comparison)
            ]
        );
        var properties = new Dictionary<string, object?>
        {
            ["join_date"] = DateOnly.Parse(joinDate, CultureInfo.InvariantCulture)
        };
        var localEvaluator = new LocalEvaluator(flags, timeProvider, NullLogger<LocalEvaluator>.Instance);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "join_date",
            distinctId: "1234b",
            personProperties: properties);

        Assert.Equal(expected, result);
    }
#endif

    [Theory]
    [InlineData("2024-01-21T16:15:49Z", ComparisonOperator.IsDateBefore, "-30h", true)]
    [InlineData("2024-01-21T16:15:51Z", ComparisonOperator.IsDateBefore, "-30h", false)]
    [InlineData("2023-12-29T22:15:49Z", ComparisonOperator.IsDateBefore, "-24d", true)]
    [InlineData("2023-12-29T22:15:51Z", ComparisonOperator.IsDateBefore, "-24d", false)]
    [InlineData("2024-01-08T22:15:49Z", ComparisonOperator.IsDateBefore, "-2w", true)]
    [InlineData("2024-01-08T22:15:51Z", ComparisonOperator.IsDateBefore, "-2w", false)]
    [InlineData("2023-12-22T22:15:49Z", ComparisonOperator.IsDateBefore, "-1m", true)]
    [InlineData("2023-12-22T22:15:51Z", ComparisonOperator.IsDateBefore, "-1m", false)]
    [InlineData("2023-01-22T22:15:49Z", ComparisonOperator.IsDateBefore, "-1y", true)]
    [InlineData("2023-01-22T22:15:51Z", ComparisonOperator.IsDateBefore, "-1y", false)]
    [InlineData("2024-01-21T16:15:49Z", ComparisonOperator.IsDateAfter, "-30h", false)]
    [InlineData("2024-01-21T16:15:51Z", ComparisonOperator.IsDateAfter, "-30h", true)]
    [InlineData("2023-12-29T22:15:49Z", ComparisonOperator.IsDateAfter, "-24d", false)]
    [InlineData("2023-12-29T22:15:51Z", ComparisonOperator.IsDateAfter, "-24d", true)]
    [InlineData("2024-01-08T22:15:49Z", ComparisonOperator.IsDateAfter, "-2w", false)]
    [InlineData("2024-01-08T22:15:51Z", ComparisonOperator.IsDateAfter, "-2w", true)]
    [InlineData("2023-12-22T22:15:49Z", ComparisonOperator.IsDateAfter, "-1m", false)]
    [InlineData("2023-12-22T22:15:51Z", ComparisonOperator.IsDateAfter, "-1m", true)]
    [InlineData("2023-01-22T22:15:49Z", ComparisonOperator.IsDateAfter, "-1y", false)]
    [InlineData("2023-01-22T22:15:51Z", ComparisonOperator.IsDateAfter, "-1y", true)]
    public void CanPerformDateComparisonCorrectlyWhenPropertyIsString(
        string overrideValue,
        ComparisonOperator comparison,
        string relativeDateString,
        bool expected)
    {
        var timeProvider = new FakeTimeProvider();
        var now = DateTimeOffset.Parse("2024-01-22T22:15:50Z", CultureInfo.InvariantCulture);
        timeProvider.SetUtcNow(now);
        var flags = CreateFlags(
            key: "join_date",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "join_date",
                    Value: new PropertyFilterValue(relativeDateString),
                    Operator: comparison)
            ]
        );
        var localEvaluator = new LocalEvaluator(flags, timeProvider, NullLogger<LocalEvaluator>.Instance);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "join_date",
            distinctId: "some-distinct-id",
            personProperties: new Dictionary<string, object?>
            {
                ["join_date"] = overrideValue
            });

        Assert.Equal(expected, result);
    }

    [Theory] // test_match_property_date_operators the timezone aware section
    [InlineData("2022-05-30", ComparisonOperator.IsDateBefore, false)]
    [InlineData("2022-03-30", ComparisonOperator.IsDateBefore, true)]
    [InlineData("2022-04-05 12:34:11 +01:00", ComparisonOperator.IsDateBefore, true)]
    [InlineData("2022-04-05 12:35:11 +02:00", ComparisonOperator.IsDateBefore, true)]
    [InlineData("2022-04-05 12:35:11 +02:00", ComparisonOperator.IsDateAfter, false)]
    [InlineData("2022-04-05 11:34:13 +00:00", ComparisonOperator.IsDateBefore, false)]
    [InlineData("2022-04-05 11:34:13 +00:00", ComparisonOperator.IsDateAfter, true)]
    public void CanPerformDateComparisonAgainstExactDate(
        string joinDate,
        ComparisonOperator comparison,
        bool expected)
    {
        var flags = CreateFlags(
            key: "join_date",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "join_date",
                    Value: new PropertyFilterValue("2022-04-05 12:34:12 +01:00"),
                    Operator: comparison)
            ]
        );
        var localEvaluator = new LocalEvaluator(flags, TimeProvider.System, NullLogger<LocalEvaluator>.Instance);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "join_date",
            distinctId: "some-distinct-id",
            personProperties: new Dictionary<string, object?>
            {
                ["join_date"] = joinDate
            });

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("not a date", ComparisonOperator.IsDateBefore)]
    [InlineData("not a date", ComparisonOperator.IsDateAfter)]
    [InlineData("", ComparisonOperator.IsDateBefore)]
    [InlineData("", ComparisonOperator.IsDateAfter)]
    [InlineData(42, ComparisonOperator.IsDateBefore)]
    [InlineData(42, ComparisonOperator.IsDateAfter)]
    [InlineData("42", ComparisonOperator.IsDateBefore)]
    [InlineData("42", ComparisonOperator.IsDateAfter)]
    public void ThrowsInconclusiveMatchExceptionWhenPropertyIsNotADate(object? joinDate, ComparisonOperator comparison)
    {
        var properties = new Dictionary<string, object?>
        {
            ["join_date"] = joinDate
        };
        var flags = CreateFlags(
            key: "join_date",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "join_date",
                    Value: new PropertyFilterValue("-30h"),
                    Operator: comparison)
            ]
        );
        var localEvaluator = new LocalEvaluator(flags, TimeProvider.System, NullLogger<LocalEvaluator>.Instance);

        Assert.Throws<InconclusiveMatchException>(() =>
        {
            localEvaluator.EvaluateFeatureFlag(
                key: "join_date",
                distinctId: "some-distinct-id",
                personProperties: properties);
        });
    }

    [Theory]
    [InlineData(ComparisonOperator.IsDateAfter)]
    [InlineData(ComparisonOperator.IsDateBefore)]
    public void ThrowsInconclusiveMatchExceptionWhenFilterValueNotDate(ComparisonOperator comparison)
    {
        var properties = new Dictionary<string, object?>
        {
            ["join_date"] = new DateTime(2024, 01, 01)
        };
        var flags = CreateFlags(
            key: "join_date",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "join_date",
                    Value: new PropertyFilterValue("some fine garbage"),
                    Operator: comparison)
            ]
        );
        var localEvaluator = new LocalEvaluator(flags, TimeProvider.System, NullLogger<LocalEvaluator>.Instance);

        Assert.Throws<InconclusiveMatchException>(() =>
        {
            localEvaluator.EvaluateFeatureFlag(
                key: "join_date",
                distinctId: "some-distinct-id",
                personProperties: properties);
        });
    }

    [Fact]
    public void ThrowsInconclusiveMatchExceptionWhenUnknownOperator()
    {
        var properties = new Dictionary<string, object?>
        {
            ["join_date"] = new DateTime(2024, 01, 01)
        };
        var flags = CreateFlags(
            key: "join_date",
            properties: [
                new PropertyFilter(
                    Type: FilterType.Person,
                    Key: "join_date",
                    Value: new PropertyFilterValue("2025-01-01"),
                    Operator: (ComparisonOperator)999)
            ]
        );
        var localEvaluator = new LocalEvaluator(flags, TimeProvider.System, NullLogger<LocalEvaluator>.Instance);

        Assert.Throws<InconclusiveMatchException>(() =>
        {
            localEvaluator.EvaluateFeatureFlag(
                key: "join_date",
                distinctId: "some-distinct-id",
                personProperties: properties);
        });
    }
}