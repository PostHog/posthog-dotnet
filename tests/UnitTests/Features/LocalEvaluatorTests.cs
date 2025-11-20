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
        return new LocalEvaluationApiResult
        {
            Flags = [
                new LocalFeatureFlag
                {
                    Id= 42,
                    TeamId= 23,
                    Name= $"{key}-feature-flag",
                    Key= key,
                    Filters=  new FeatureFlagFilters {
                        Groups = [
                            new FeatureFlagGroup
                            {
                                Properties = properties
                            }
                        ]
                    }
                }
            ],
            GroupTypeMapping = new Dictionary<string, string>()
        };
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
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "email",
                    Value = new PropertyFilterValue([
                        "tyrion@example.com",
                        "danaerys@example.com",
                        "sansa@example.com",
                        "ned@example.com"
                    ]),
                    Operator = comparison
                }
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
    [InlineData("internal/1234", ComparisonOperator.Exact, true)]
    [InlineData("INTERNAL/1234", ComparisonOperator.Exact, true)] // Case-insensitive
    [InlineData("public/98765", ComparisonOperator.Exact, false)]
    [InlineData("", ComparisonOperator.Exact, false)]
    [InlineData(null, ComparisonOperator.Exact, false)]
    [InlineData("internal/1234", ComparisonOperator.IsNot, false)]
    [InlineData("INTERNAL/1234", ComparisonOperator.IsNot, false)] // Case-insensitive
    [InlineData("public/98765", ComparisonOperator.IsNot, true)]
    [InlineData("", ComparisonOperator.IsNot, true)]
    [InlineData(null, ComparisonOperator.IsNot, true)]
    public void HandlesMatchesByDistinctId(string? distinctId, ComparisonOperator comparison, bool expected)
    {
        var flags = CreateFlags(
            key: "valid_users",
            properties: [
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "distinct_id",
                    Value = new PropertyFilterValue([
                        "internal/123",
                        "internal/1234",
                        "public/12345",
                        "public/56789"
                    ]),
                    Operator = comparison
                }
            ]
        );
        var properties = new Dictionary<string, object?>();
        var localEvaluator = new LocalEvaluator(flags);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "valid_users",
            distinctId: distinctId ?? string.Empty,
            personProperties: properties);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("internal/1234", false)]
    [InlineData("INTERNAL/1234", false)]
    [InlineData("public/98765", false)]
    [InlineData("internal/9999", true)]
    [InlineData("INTERNAL/9999", true)] // insensitive match
    public void HandlesMatchByDistinctIdWithPropertyPresent(string? distinctId, bool expected)
    {
        var flags = CreateFlags(
            key: "valid_users",
            properties: [
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "distinct_id",
                    Value = new PropertyFilterValue([
                        distinctId ?? "unknown",
                        "internal/1234",
                        "public/12345",
                        "public/56789"
                    ]),
                    Operator = ComparisonOperator.Exact
                }
            ]
        );
        var properties = new Dictionary<string, object?>
        {
            ["distinct_id"] = "internal/9999"
        };
        var localEvaluator = new LocalEvaluator(flags);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "valid_users",
            distinctId: distinctId ?? string.Empty,
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
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "age",
                    Value = new PropertyFilterValue([
                        "4", "8", "15", "16", "23", "42", "42.5"
                    ]),
                    Operator = comparison
                }
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
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "email",
                    Value = new PropertyFilterValue("is_set"),
                    Operator = ComparisonOperator.IsSet
                }
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
    [InlineData("test@posthog.com")]
    [InlineData("")]
    [InlineData(null)]
    public void ThrowsInconclusiveMatchExceptionWhenOperatorIsIsNotSet(string? email)
    {
        var flags = CreateFlags(
            key: "email",
            properties: [
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "email",
                    Value = new PropertyFilterValue("is_not_set"),
                    Operator = ComparisonOperator.IsNotSet
                }
            ]
        );
        var properties = new Dictionary<string, object?>
        {
            ["email"] = email
        };
        var localEvaluator = new LocalEvaluator(flags);

        Assert.Throws<InconclusiveMatchException>(() => localEvaluator.EvaluateFeatureFlag(
            key: "email",
            distinctId: "1234",
            personProperties: properties));
    }

    [Fact]
    public void ThrowsInconclusiveMatchExceptionWhenKeyDoesNotMatch()
    {
        var flags = CreateFlags(
            key: "email",
            properties: [
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "email",
                    Value = new PropertyFilterValue("is_set"),
                    Operator = ComparisonOperator.IsSet
                }
            ]
        );
        var localEvaluator = new LocalEvaluator(flags);

        Assert.Throws<InconclusiveMatchException>(() => localEvaluator.EvaluateFeatureFlag(
            key: "email",
            distinctId: "1234",
            personProperties: new()
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
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "email",
                    Value = new PropertyFilterValue(filterValue),
                    Operator = comparison
                }
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
    [InlineData("Works at PostHog", ComparisonOperator.DoesNotContainIgnoreCase, "\"posthog\"", false)]
    [InlineData("Works at PostHog", ComparisonOperator.DoesNotContainIgnoreCase, "\"PostHog\"", false)]
    [InlineData("Loves puppies", ComparisonOperator.ContainsIgnoreCase, "\"cats\"", false)]
    [InlineData("Loves puppies", ComparisonOperator.DoesNotContainIgnoreCase, "\"cats\"", true)]
    public void HandlesContainsComparisons(object overrideValue, ComparisonOperator comparison, string filterValueJson, bool expected)
    {
        var flags = CreateFlags(
            key: "bio",
            properties:
            [
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "bio",
                    Value = PropertyFilterValue.Create(JsonDocument.Parse(filterValueJson).RootElement)!,
                    Operator = comparison
                }
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
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "age",
                    Value = PropertyFilterValue.Create(JsonDocument.Parse(filterValueJson).RootElement)!,
                    Operator = comparison
                }
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
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "join_date",
                    Value = new PropertyFilterValue(relativeDateString),
                    Operator = comparison
                }
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
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "join_date",
                    Value = new PropertyFilterValue(relativeDateString),
                    Operator = comparison
                }
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
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "join_date",
                    Value = new PropertyFilterValue(relativeDateString),
                    Operator = comparison
                }
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
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "join_date",
                    Value = new PropertyFilterValue("2022-04-05 12:34:12 +01:00"),
                    Operator = comparison
                }
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
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "join_date",
                    Value = new PropertyFilterValue("-30h"),
                    Operator = comparison
                }
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
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "join_date",
                    Value = new PropertyFilterValue("some fine garbage"),
                    Operator = comparison
                }
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
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "join_date",
                    Value = new PropertyFilterValue("2025-01-01"),
                    Operator = (ComparisonOperator)999
                }
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

public class TheFlagDependencyEvaluationMethod
{
    static LocalEvaluationApiResult CreateFlagsWithDependencies(
        Dictionary<string, LocalFeatureFlag> flags)
    {
        return new LocalEvaluationApiResult
        {
            Flags = flags.Values.ToArray(),
            GroupTypeMapping = new Dictionary<string, string>()
        };
    }

    static LocalFeatureFlag CreateSimpleFlag(string key, bool active = true)
    {
        return new LocalFeatureFlag
        {
            Id = 42,
            TeamId = 23,
            Name = $"{key}-feature-flag",
            Key = key,
            Active = active,
            Filters = new FeatureFlagFilters
            {
                Groups = [
                    new FeatureFlagGroup
                    {
                        Properties = [],
                        RolloutPercentage = 100
                    }
                ]
            }
        };
    }

    static LocalFeatureFlag CreateFlagWithDependency(string key, string dependencyKey, bool expectedValue, IReadOnlyList<string>? dependencyChain = null)
    {
        return new LocalFeatureFlag
        {
            Id = 42,
            TeamId = 23,
            Name = $"{key}-feature-flag",
            Key = key,
            Active = true,
            Filters = new FeatureFlagFilters
            {
                Groups = [
                    new FeatureFlagGroup
                    {
                        Properties = [
                            new PropertyFilter
                            {
                                Type = FilterType.Flag,
                                Key = dependencyKey,
                                Value = new PropertyFilterValue(expectedValue),
                                Operator = ComparisonOperator.FlagEvaluatesTo,
                                DependencyChain = dependencyChain
                            }
                        ],
                        RolloutPercentage = 100
                    }
                ]
            }
        };
    }

    static LocalFeatureFlag CreateFlagWithDependencyOnVariant(
        string key,
        string dependencyKey,
        string expectedVariant,
        IReadOnlyList<string>? dependencyChain = null)
    {
        return new LocalFeatureFlag
        {
            Id = 42,
            TeamId = 23,
            Name = $"{key}-feature-flag",
            Key = key,
            Active = true,
            Filters = new FeatureFlagFilters
            {
                Groups = [
                    new FeatureFlagGroup
                    {
                        Properties = [
                            new PropertyFilter
                            {
                                Type = FilterType.Flag,
                                Key = dependencyKey,
                                Value = new PropertyFilterValue(expectedVariant),
                                Operator = ComparisonOperator.FlagEvaluatesTo,
                                DependencyChain = dependencyChain
                            }
                        ],
                        RolloutPercentage = 100
                    }
                ]
            }
        };
    }

    static LocalFeatureFlag CreateMultivariateFlagWithDependencyOnVariant(
        string key,
        string dependencyKey,
        string expectedVariant,
        IReadOnlyList<string> dependencyChain,
        params string[] variantKeys)
    {
        var variants = variantKeys.Select((variantKey, _) => new Variant
        {
            Key = variantKey,
            Name = $"Variant {variantKey}",
            RolloutPercentage = 100.0 / variantKeys.Length
        }).ToArray();

        return new LocalFeatureFlag
        {
            Id = 43,
            TeamId = 23,
            Name = $"{key}-feature-flag",
            Key = key,
            Active = true,
            Filters = new FeatureFlagFilters
            {
                Groups = [
                    new FeatureFlagGroup
                    {
                        Properties = [
                            new PropertyFilter
                            {
                                Type = FilterType.Flag,
                                Key = dependencyKey,
                                Value = new PropertyFilterValue(expectedVariant),
                                Operator = ComparisonOperator.FlagEvaluatesTo,
                                DependencyChain = dependencyChain
                            }
                        ],
                        RolloutPercentage = 100
                    }
                ],
                Multivariate = new Multivariate
                {
                    Variants = variants
                }
            }
        };
    }

    static LocalFeatureFlag CreateMultivariateFlagWithVariants(
        string key,
        bool active = true,
        params string[] variantKeys)
    {
        var variants = variantKeys.Select((variantKey, _) => new Variant
        {
            Key = variantKey,
            Name = $"Variant {variantKey}",
            RolloutPercentage = 100.0 / variantKeys.Length
        }).ToArray();

        // Create conditions so we can explicitly target variants
        var filterGroups = variants.Select(variant =>
            new FeatureFlagGroup
            {
                Variant = variant.Key,
                Properties = [
                    new PropertyFilter
                    {
                        Type = FilterType.Person,
                        Key = "email",
                        Value = new PropertyFilterValue(variant.Key + "@example.com"),
                        Operator = ComparisonOperator.Exact
                    }
                ],
                RolloutPercentage = 100
            }).ToList();
        filterGroups.Add(
            new FeatureFlagGroup
            {
                Properties = [],
                RolloutPercentage = 100
            });


        return new LocalFeatureFlag
        {
            Id = 44,
            TeamId = 23,
            Name = $"{key}-feature-flag",
            Key = key,
            Active = active,
            Filters = new FeatureFlagFilters
            {
                Groups = filterGroups,
                Multivariate = new Multivariate
                {
                    Variants = variants
                }
            }
        };
    }

    [Fact]
    public void TestsSimpleFlagEvaluation()
    {
        var simpleFlag = CreateSimpleFlag("simple-flag", active: true);

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["simple-flag"] = simpleFlag
        });

        var localEvaluator = new LocalEvaluator(flags);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "simple-flag",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?>());

        Assert.True(result.Value); // This should be true for a simple active flag
    }

    [Fact]
    public void EvaluatesSimpleFlagDependency()
    {
        var dependencyFlag = CreateSimpleFlag("dependency-flag", active: true);
        var mainFlag = CreateFlagWithDependency("main-flag", "dependency-flag", expectedValue: true, ["dependency-flag"]);

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["dependency-flag"] = dependencyFlag,
            ["main-flag"] = mainFlag
        });

        var localEvaluator = new LocalEvaluator(flags);

        // First, let's make sure the dependency flag evaluates correctly on its own
        var dependencyResult = localEvaluator.EvaluateFeatureFlag(
            key: "dependency-flag",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?>());

        Assert.True(dependencyResult.Value); // This should be true

        // Now test the main flag that depends on dependency-flag
        var result = localEvaluator.EvaluateFeatureFlag(
            key: "main-flag",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?>());


        Assert.True(result.Value);
    }

    [Fact]
    public void ReturnsFalseWhenDependencyDoesNotMatch()
    {
        var dependencyFlag = CreateSimpleFlag("dependency-flag", active: false); // Inactive dependency
        var mainFlag = CreateFlagWithDependency("main-flag", "dependency-flag", expectedValue: true, ["dependency-flag"]);

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["dependency-flag"] = dependencyFlag,
            ["main-flag"] = mainFlag
        });

        var localEvaluator = new LocalEvaluator(flags);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "main-flag",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?>());

        Assert.False(result.Value);
    }

    [Fact]
    public void HandlesCircularDependenciesWithEmptyChain()
    {
        // Create flags with circular dependency (empty chain)
        var flagA = new LocalFeatureFlag
        {
            Id = 42,
            TeamId = 23,
            Name = "flag-a",
            Key = "flag-a",
            Active = true,
            Filters = new FeatureFlagFilters
            {
                Groups = [
                    new FeatureFlagGroup
                    {
                        Properties = [
                            new PropertyFilter
                            {
                                Type = FilterType.Flag,
                                Key = "flag-b",
                                Value = new PropertyFilterValue(true),
                                Operator = ComparisonOperator.FlagEvaluatesTo,
                                DependencyChain = [] // Empty chain indicates circular dependency
                            }
                        ],
                        RolloutPercentage = 100
                    }
                ]
            }
        };

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["flag-a"] = flagA,
        });

        var localEvaluator = new LocalEvaluator(flags);

        Assert.Throws<InconclusiveMatchException>(() =>
            localEvaluator.EvaluateFeatureFlag(
                key: "flag-a",
                distinctId: "test-user",
                personProperties: new Dictionary<string, object?>())
        );
    }

    [Fact]
    public void ThrowsInconclusiveMatchExceptionWhenDependencyFlagNotFound()
    {
        var mainFlag = CreateFlagWithDependency("main-flag", "non-existent-flag", expectedValue: true, ["non-existent-flag"]);

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["main-flag"] = mainFlag
        });

        var localEvaluator = new LocalEvaluator(flags);

        Assert.Throws<InconclusiveMatchException>(() =>
            localEvaluator.EvaluateFeatureFlag(
                key: "main-flag",
                distinctId: "test-user",
                personProperties: new Dictionary<string, object?>()));
    }

    [Fact]
    public void EvaluatesMultiLevelDependencyChain()
    {
        var flagA = CreateSimpleFlag("flag-a", active: true);
        var flagB = CreateFlagWithDependency("flag-b", "flag-a", expectedValue: true, ["flag-a"]);
        var flagC = CreateFlagWithDependency("flag-c", "flag-b", expectedValue: true, ["flag-a", "flag-b"]);

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["flag-a"] = flagA,
            ["flag-b"] = flagB,
            ["flag-c"] = flagC
        });

        var localEvaluator = new LocalEvaluator(flags);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "flag-c",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?>());

        Assert.True(result.Value);
    }

    [Fact]
    public void ReturnsFalseWhenEarlyDependencyInChainFails()
    {
        var flagA = CreateSimpleFlag("flag-a", active: false); // This will cause the chain to fail
        var flagB = CreateFlagWithDependency("flag-b", "flag-a", expectedValue: true, ["flag-a"]);
        var flagC = CreateFlagWithDependency("flag-c", "flag-b", expectedValue: true, ["flag-a", "flag-b"]);

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["flag-a"] = flagA,
            ["flag-b"] = flagB,
            ["flag-c"] = flagC
        });

        var localEvaluator = new LocalEvaluator(flags);

        var result = localEvaluator.EvaluateFeatureFlag(
            key: "flag-c",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?>());

        Assert.False(result.Value);
    }

    [Fact]
    public void ThrowsInconclusiveMatchExceptionWhenDependencyChainFlagNotFound()
    {
        var mainFlag = CreateFlagWithDependency("main-flag", "non-existent-flag", expectedValue: true, ["non-existent-flag"]);

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["main-flag"] = mainFlag
        });

        var localEvaluator = new LocalEvaluator(flags);

        // This should throw because the dependency chain references a non-existent flag
        Assert.Throws<InconclusiveMatchException>(() =>
            localEvaluator.EvaluateFeatureFlag(
                key: "main-flag",
                distinctId: "test-user",
                personProperties: new Dictionary<string, object?>()));
    }

    [Fact]
    public void EvaluatesFlagDependencyWithPersonPropertiesCorrectly()
    {
        // Create a dependency flag that depends on email property
        var dependencyFlag = new LocalFeatureFlag
        {
            Id = 1,
            TeamId = 1,
            Name = "dependency-flag",
            Key = "dependency-flag",
            Active = true,
            Filters = new FeatureFlagFilters
            {
                Groups =
                [
                    new FeatureFlagGroup
                    {
                        Properties =
                        [
                            new PropertyFilter
                            {
                                Key = "email",
                                Value = new PropertyFilterValue("phil@example.com"),
                                Operator = ComparisonOperator.Exact,
                                Type = FilterType.Person
                            }
                        ],
                        RolloutPercentage = 100
                    }
                ]
            }
        };

        // Create main flag that depends on the dependency flag
        var mainFlag = CreateFlagWithDependency("test-flag-dependency", "dependency-flag", expectedValue: true, ["dependency-flag"]);

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["dependency-flag"] = dependencyFlag,
            ["test-flag-dependency"] = mainFlag
        });

        var localEvaluator = new LocalEvaluator(flags);

        // Test with matching email
        var result = localEvaluator.EvaluateFeatureFlag(
            key: "test-flag-dependency",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?> { ["email"] = "phil@example.com" });

        Assert.True(result.Value); // Should be true because dependency matches

        // Test with non-matching email
        var result2 = localEvaluator.EvaluateFeatureFlag(
            key: "test-flag-dependency",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?> { ["email"] = "other@example.com" });

        Assert.False(result2.Value); // Should be false because dependency doesn't match
    }

    [Fact]
    public void EvaluatesMultivariateFlagDependencyAgainstSpecificVariant()
    {
        // Create a multivariate leaf flag with "control" and "test" variants
        var leafFlag = CreateMultivariateFlagWithVariants("leaf-flag", active: true, "control", "test");

        // Create dependent flag that checks if leaf-flag evaluates to "control"
        var dependentFlag = CreateFlagWithDependencyOnVariant("dependent-flag", "leaf-flag", "control", ["leaf-flag"]);

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["leaf-flag"] = leafFlag,
            ["dependent-flag"] = dependentFlag
        });

        var localEvaluator = new LocalEvaluator(flags);

        // Make sure the leaf flag evaluates to the variant we expect
        Assert.Equal("control", localEvaluator.EvaluateFeatureFlag(
            key: "leaf-flag",
            distinctId: "user-control",
            personProperties: new()
            {
                ["email"] = "control@example.com"
            }));
        Assert.Equal("test", localEvaluator.EvaluateFeatureFlag(
            key: "leaf-flag",
            distinctId: "user-control",
            personProperties: new()
            {
                ["email"] = "test@example.com"
            }));

        // Test when leaf-flag evaluates to "control" variant - dependent should be true
        Assert.True(localEvaluator.EvaluateFeatureFlag(
            key: "dependent-flag",
            distinctId: "user-control",
            personProperties: new()
            {
                ["email"] = "control@example.com"
            }).Value);

        // Test when leaf-flag evaluates to "test" variant - dependent should be false
        Assert.False(localEvaluator.EvaluateFeatureFlag(
            key: "dependent-flag",
            distinctId: "user-test",
            personProperties: new()
            {
                ["email"] = "test@example.com"
            }).Value);
    }

    [Fact]
    public void EvaluatesMultivariateFlagDependencyAgainstBooleanTrue()
    {
        // Create a multivariate leaf flag
        var leafFlag = CreateMultivariateFlagWithVariants("leaf-flag", active: true, "control", "test");

        // Create dependent flag that checks if leaf-flag evaluates to true (any variant)
        var dependentFlag = CreateFlagWithDependency("dependent-flag", "leaf-flag", expectedValue: true, ["leaf-flag"]);

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["leaf-flag"] = leafFlag,
            ["dependent-flag"] = dependentFlag
        });

        var localEvaluator = new LocalEvaluator(flags);

        // Test when leaf-flag evaluates to "control" variant - dependent should be true
        Assert.True(localEvaluator.EvaluateFeatureFlag(
            key: "dependent-flag",
            distinctId: "user-control",
            personProperties: new()
            {
                ["email"] = "control@example.com"
            }).Value);

        // Test when leaf-flag evaluates to "test" variant - dependent should be true
        Assert.True(localEvaluator.EvaluateFeatureFlag(
            key: "dependent-flag",
            distinctId: "user-test",
            personProperties: new()
            {
                ["email"] = "test@example.com"
            }).Value);

        // Test with inactive leaf flag - should make dependent false
        var inactiveLeafFlag = CreateMultivariateFlagWithVariants("leaf-flag", active: false, "control", "test");

        // Create dependent flag that checks if leaf-flag evaluates to true (any variant)
        var dependentOnInactiveFlag = CreateFlagWithDependency("dependent-flag", "leaf-flag", expectedValue: true, ["leaf-flag"]);

        var flagsWithInactive = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["leaf-flag"] = inactiveLeafFlag,
            ["dependent-flag"] = dependentOnInactiveFlag
        });
        var localEvaluatorWithInactive = new LocalEvaluator(flagsWithInactive);
        // Since inactive-leaf-flag evaluates to false, and dependent expects false, result should be true
        Assert.False(localEvaluatorWithInactive.EvaluateFeatureFlag(
            key: "dependent-flag",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?>()).Value);
    }

    [Fact]
    public void EvaluatesMultivariateFlagDependencyAgainstBooleanFalse()
    {
        // Create a multivariate leaf flag  
        var leafFlag = CreateMultivariateFlagWithVariants("leaf-flag", active: true, "control", "test");

        // Create dependent flag that checks if leaf-flag evaluates to false
        var dependentFlag = CreateFlagWithDependency("dependent-flag", "leaf-flag", expectedValue: false, ["leaf-flag"]);

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["leaf-flag"] = leafFlag,
            ["dependent-flag"] = dependentFlag
        });

        var localEvaluator = new LocalEvaluator(flags);

        // Test when leaf-flag evaluates to "control" variant - dependent should be true
        Assert.False(localEvaluator.EvaluateFeatureFlag(
            key: "dependent-flag",
            distinctId: "user-control",
            personProperties: new Dictionary<string, object?>()).Value);

        // Test when leaf-flag evaluates to "test" variant - dependent should be true
        Assert.False(localEvaluator.EvaluateFeatureFlag(
            key: "dependent-flag",
            distinctId: "user-test",
            personProperties: new Dictionary<string, object?>()).Value);

        // Test with inactive leaf flag - should make dependent true since leaf evaluates to false
        var inactiveLeafFlag = CreateMultivariateFlagWithVariants("inactive-leaf-flag", active: false, "control", "test");
        var dependentFlagForInactive = CreateFlagWithDependency("dependent-flag-for-inactive", "inactive-leaf-flag", expectedValue: false, ["inactive-leaf-flag"]);

        var flagsWithInactive = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["inactive-leaf-flag"] = inactiveLeafFlag,
            ["dependent-flag-for-inactive"] = dependentFlagForInactive
        });

        var localEvaluatorWithInactive = new LocalEvaluator(flagsWithInactive);

        // Since inactive-leaf-flag evaluates to false, and dependent expects false, result should be true
        Assert.True(localEvaluatorWithInactive.EvaluateFeatureFlag(
            key: "dependent-flag-for-inactive",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?>()).Value);
    }

    [Fact]
    public void EvaluatesMultiLevelMultivariateDependencyChain()
    {
        // Create a multivariate leaf flag with "control" and "test" variants
        var leafFlag = CreateMultivariateFlagWithVariants("leaf-flag", active: true, "control", "test");
        var intermediateFlag = CreateMultivariateFlagWithDependencyOnVariant(
            "intermediate-flag",
            "leaf-flag",
            "control",
            ["leaf-flag"],
            "blue", "green");
        var dependentFlag = CreateFlagWithDependencyOnVariant(
            "dependent-flag",
            "intermediate-flag",
            "blue",
            ["leaf-flag", "intermediate-flag"]);

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["leaf-flag"] = leafFlag,
            ["intermediate-flag"] = intermediateFlag,
            ["dependent-flag"] = dependentFlag
        });

        var localEvaluator = new LocalEvaluator(flags);

        // Make sure the leaf flag evaluates to the variant we expect
        Assert.Equal("control", localEvaluator.EvaluateFeatureFlag(
            key: "leaf-flag",
            distinctId: "user-control",
            personProperties: new()
            {
                ["email"] = "control@example.com"
            }));
        Assert.Equal("test", localEvaluator.EvaluateFeatureFlag(
            key: "leaf-flag",
            distinctId: "user-test",
            personProperties: new()
            {
                ["email"] = "test@example.com"
            }));
        // Make sure the intermediate flag evaluates to the expected variant
        Assert.Equal("blue", localEvaluator.EvaluateFeatureFlag(
            key: "intermediate-flag",
            distinctId: "blue-distinct-id", // Just happens to align with "blue" variant.
            personProperties: new()
            {
                ["email"] = "control@example.com"
            }));

        // Make sure the intermediate flag evaluates to the expected variant
        Assert.Equal("green", localEvaluator.EvaluateFeatureFlag(
            key: "intermediate-flag",
            distinctId: "green-distinct-id", // Just happens to align with "green" variant.
            personProperties: new()
            {
                ["email"] = "control@example.com"
            }));

        // Make sure the intermediate flag evaluates to false when leaf is "test"
        Assert.False(localEvaluator.EvaluateFeatureFlag(
            key: "intermediate-flag",
            distinctId: "green-distinct-id", // Just happens to align with "green" variant.
            personProperties: new()
            {
                ["email"] = "test@example.com"
            }).Value);

        // Test when leaf-flag evaluates to "control" variant, intermediate is "blue", and dependent should be true
        Assert.True(localEvaluator.EvaluateFeatureFlag(
            key: "dependent-flag",
            distinctId: "blue-distinct-id",
            personProperties: new()
            {
                ["email"] = "control@example.com"
            }).Value);

        // Test when leaf-flag evaluates to "control" variant, intermediate is "green", and dependent should be false
        Assert.False(localEvaluator.EvaluateFeatureFlag(
            key: "dependent-flag",
            distinctId: "green-distinct-id",
            personProperties: new()
            {
                ["email"] = "control@example.com"
            }).Value);

        // Test when leaf-flag evaluates to "test" variant, intermediate is False, and dependent should be false
        Assert.False(localEvaluator.EvaluateFeatureFlag(
            key: "dependent-flag",
            distinctId: "green-distinct-id",
            personProperties: new()
            {
                ["email"] = "test@example.com"
            }).Value);
    }

    [Fact]
    public void EvaluatesFlagWithCombinedDependencyAndPersonPropertyConditions()
    {
        // Create a simple dependency flag that's always active
        var dependencyFlag = CreateSimpleFlag("dependency-flag", active: true);

        // Create a flag that requires BOTH:
        // 1. dependency-flag to evaluate to true (flag dependency)
        // 2. email property to match specific value (person property filter)
        var combinedFlag = new LocalFeatureFlag
        {
            Id = 42,
            TeamId = 23,
            Name = "combined-flag",
            Key = "combined-flag",
            Active = true,
            Filters = new FeatureFlagFilters
            {
                Groups = [
                    new FeatureFlagGroup
                    {
                        Properties = [
                            // Flag dependency condition
                            new PropertyFilter
                            {
                                Type = FilterType.Flag,
                                Key = "dependency-flag",
                                Value = new PropertyFilterValue(true),
                                Operator = ComparisonOperator.FlagEvaluatesTo,
                                DependencyChain = ["dependency-flag"]
                            },
                            // Person property condition
                            new PropertyFilter
                            {
                                Type = FilterType.Person,
                                Key = "email",
                                Value = new PropertyFilterValue("test@example.com"),
                                Operator = ComparisonOperator.Exact
                            }
                        ],
                        RolloutPercentage = 100
                    }
                ]
            }
        };

        var flags = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["dependency-flag"] = dependencyFlag,
            ["combined-flag"] = combinedFlag
        });

        var localEvaluator = new LocalEvaluator(flags);

        // Test case 1: Both conditions match - should return true
        var result1 = localEvaluator.EvaluateFeatureFlag(
            key: "combined-flag",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?> { ["email"] = "test@example.com" });

        Assert.True(result1.Value);

        // Test case 2: Flag dependency matches but person property doesn't - should return false
        var result2 = localEvaluator.EvaluateFeatureFlag(
            key: "combined-flag",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?> { ["email"] = "other@example.com" });

        Assert.False(result2.Value);

        // Test case 3: Person property matches but flag dependency doesn't
        // Create inactive dependency flag for this test
        var inactiveDependencyFlag = CreateSimpleFlag("inactive-dependency-flag", active: false);
        var combinedFlagWithInactiveDep = new LocalFeatureFlag
        {
            Id = 43,
            TeamId = 23,
            Name = "combined-flag-inactive-dep",
            Key = "combined-flag-inactive-dep",
            Active = true,
            Filters = new FeatureFlagFilters
            {
                Groups = [
                    new FeatureFlagGroup
                    {
                        Properties = [
                            new PropertyFilter
                            {
                                Type = FilterType.Flag,
                                Key = "inactive-dependency-flag",
                                Value = new PropertyFilterValue(true),
                                Operator = ComparisonOperator.FlagEvaluatesTo,
                                DependencyChain = ["inactive-dependency-flag"]
                            },
                            new PropertyFilter
                            {
                                Type = FilterType.Person,
                                Key = "email",
                                Value = new PropertyFilterValue("test@example.com"),
                                Operator = ComparisonOperator.Exact
                            }
                        ],
                        RolloutPercentage = 100
                    }
                ]
            }
        };

        var flagsWithInactiveDep = CreateFlagsWithDependencies(new Dictionary<string, LocalFeatureFlag>
        {
            ["inactive-dependency-flag"] = inactiveDependencyFlag,
            ["combined-flag-inactive-dep"] = combinedFlagWithInactiveDep
        });

        var localEvaluatorWithInactiveDep = new LocalEvaluator(flagsWithInactiveDep);

        var result3 = localEvaluatorWithInactiveDep.EvaluateFeatureFlag(
            key: "combined-flag-inactive-dep",
            distinctId: "test-user",
            personProperties: new Dictionary<string, object?> { ["email"] = "test@example.com" });

        Assert.False(result3.Value);
    }

    [Fact]
    public void PropertyFilterEqualityIsSymmetricForDependencyChain()
    {
        // Test the asymmetric equality bug identified by greptile
        var filterWithNullDependencyChain = new PropertyFilter
        {
            Type = FilterType.Flag,
            Key = "test-flag",
            Value = new PropertyFilterValue(true),
            Operator = ComparisonOperator.FlagEvaluatesTo,
            DependencyChain = null
        };

        var filterWithEmptyDependencyChain = new PropertyFilter
        {
            Type = FilterType.Flag,
            Key = "test-flag",
            Value = new PropertyFilterValue(true),
            Operator = ComparisonOperator.FlagEvaluatesTo,
            DependencyChain = []
        };

        var filterWithDependencyChain = new PropertyFilter
        {
            Type = FilterType.Flag,
            Key = "test-flag",
            Value = new PropertyFilterValue(true),
            Operator = ComparisonOperator.FlagEvaluatesTo,
            DependencyChain = ["dependency-flag"]
        };

        // Test symmetry: A.Equals(B) should equal B.Equals(A)
        Assert.Equal(filterWithNullDependencyChain.Equals(filterWithEmptyDependencyChain),
                     filterWithEmptyDependencyChain.Equals(filterWithNullDependencyChain));

        Assert.Equal(filterWithNullDependencyChain.Equals(filterWithDependencyChain),
                     filterWithDependencyChain.Equals(filterWithNullDependencyChain));

        Assert.Equal(filterWithEmptyDependencyChain.Equals(filterWithDependencyChain),
                     filterWithDependencyChain.Equals(filterWithEmptyDependencyChain));

        // Test specific expected behaviors
        Assert.True(filterWithNullDependencyChain.Equals(filterWithEmptyDependencyChain)); // null should equal empty
        Assert.False(filterWithNullDependencyChain.Equals(filterWithDependencyChain)); // null should not equal non-empty
        Assert.False(filterWithEmptyDependencyChain.Equals(filterWithDependencyChain)); // empty should not equal non-empty
    }
}

public class TheMatchesDependencyValueMethod
{
    [Theory]
    // String variant matches string exactly (case-sensitive)
    [InlineData("control", "control", true)]
    [InlineData("Control", "Control", true)]
    [InlineData("control", "Control", false)]
    [InlineData("Control", "CONTROL", false)]
    [InlineData("control", "test", false)]
    public void MatchesStringVariantExactly(string expectedString, string actualString, bool shouldMatch)
    {
        var expectedValue = new PropertyFilterValue(expectedString);
        var actualValue = new StringOrValue<bool>(actualString);

        var result = LocalEvaluator.MatchesDependencyValue(expectedValue, actualValue);

        Assert.Equal(shouldMatch, result);
    }

    [Theory]
    // String variant matches boolean true (any variant is truthy)
    [InlineData(true, "control", true)]
    [InlineData(true, "test", true)]
    [InlineData(false, "control", false)]
    public void MatchesStringVariantAgainstBoolean(bool expectedBoolean, string actualString, bool shouldMatch)
    {
        var expectedValue = new PropertyFilterValue(expectedBoolean);
        var actualValue = new StringOrValue<bool>(actualString);

        var result = LocalEvaluator.MatchesDependencyValue(expectedValue, actualValue);

        Assert.Equal(shouldMatch, result);
    }

    [Theory]
    // Boolean matches boolean exactly
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void MatchesBooleanExactly(bool expectedBoolean, bool actualBoolean, bool shouldMatch)
    {
        var expectedValue = new PropertyFilterValue(expectedBoolean);
        var actualValue = new StringOrValue<bool>(actualBoolean);

        var result = LocalEvaluator.MatchesDependencyValue(expectedValue, actualValue);

        Assert.Equal(shouldMatch, result);
    }

    [Fact]
    public void DoesNotMatchEmptyString()
    {
        // Empty string doesn't match boolean true
        var expectedValue1 = new PropertyFilterValue(true);
        var actualValue1 = new StringOrValue<bool>("");
        var result1 = LocalEvaluator.MatchesDependencyValue(expectedValue1, actualValue1);
        Assert.False(result1);

        // Empty string doesn't match string "control"
        var expectedValue2 = new PropertyFilterValue("control");
        var actualValue2 = new StringOrValue<bool>("");
        var result2 = LocalEvaluator.MatchesDependencyValue(expectedValue2, actualValue2);
        Assert.False(result2);
    }

    [Theory]
    // Type mismatches - these test cases where the implementation should return false
    [InlineData(123, "control", false)] // Long expected value vs string actual
    [InlineData("control", true, false)] // String expected vs boolean actual  
    public void DoesNotMatchTypeMismatches(object expected, object actual, bool shouldMatch)
    {
        PropertyFilterValue expectedValue = expected switch
        {
            int i => new PropertyFilterValue((long)i),
            long l => new PropertyFilterValue(l),
            string s => new PropertyFilterValue(s),
            bool b => new PropertyFilterValue(b),
            _ => throw new ArgumentException("Unsupported type for test")
        };

        StringOrValue<bool> actualValue = actual switch
        {
            string s => new StringOrValue<bool>(s),
            bool b => new StringOrValue<bool>(b),
            _ => throw new ArgumentException("Unsupported type for test")
        };

        var result = LocalEvaluator.MatchesDependencyValue(expectedValue, actualValue);

        Assert.Equal(shouldMatch, result);
    }
}