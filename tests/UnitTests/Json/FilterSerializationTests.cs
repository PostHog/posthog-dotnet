using PostHog.Api;
using PostHog.Json;
namespace FilterSerializationTests;

public class TheDeserializeAsyncMethod
{
    [Fact]
    public async Task CanDeserializeFilterPropertyValue()
    {
        var json = """
                   [
                   "tyrion@example.com",
                   "danaerys@example.com",
                   "sansa@example.com",
                   "ned@example.com"
                   ]
                   """;
        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<PropertyFilterValue>(json);

        Assert.NotNull(result);
        Assert.Equal([
            "tyrion@example.com",
            "danaerys@example.com",
            "sansa@example.com",
            "ned@example.com"
        ], result.ListOfStrings);
    }

    [Fact]
    public async Task CanDeserializePropertyFilter()
    {
        var json = """
                   {
                       "key": "email",
                       "type": "person",
                       "value": [
                           "tyrion@example.com",
                           "danaerys@example.com",
                           "sansa@example.com",
                           "ned@example.com"
                       ],
                       "operator": "exact"
                   }
                   """;
        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<Filter>(json);

        var propertyFilter = Assert.IsType<PropertyFilter>(result);
        Assert.Equal(
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
                Operator = ComparisonOperator.Exact
            },
            propertyFilter);
    }

    [Fact]
    public async Task CanDeserializeFlagDependencyInCohort()
    {
        const string json = """
            {"type": "flag", "key": "checkout", "operator": "flag_evaluates_to",
             "value": true, "dependency_chain": ["checkout"]}
            """;

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<Filter>(json);

        var propertyFilter = Assert.IsType<PropertyFilter>(result);
        Assert.Equal(FilterType.Flag, propertyFilter.Type);
        Assert.Equal("checkout", propertyFilter.Key);
        Assert.Equal(ComparisonOperator.FlagEvaluatesTo, propertyFilter.Operator);
        Assert.Equal(new[] { "checkout" }, propertyFilter.DependencyChain);
    }

    [Theory]
    [InlineData("""{"type":"future_filter","negation":true,"custom":{"value":[1,null,"test"]}}""")]
    [InlineData("""{"type":"XOR","values":[{"type":"person","key":"plan","value":"pro","operator":"exact"}]}""")]
    public async Task PreservesUnrecognizedFilterWhenRoundTripping(string json)
    {
        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<Filter>(json);
        Assert.NotNull(result);

        var serialized = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(result);

        JsonAssert.Equal(json, serialized);
    }

    [Theory]
    [InlineData("starts_with", ComparisonOperator.StartsWith)]
    [InlineData("not_starts_with", ComparisonOperator.NotStartsWith)]
    [InlineData("ends_with", ComparisonOperator.EndsWith)]
    [InlineData("not_ends_with", ComparisonOperator.NotEndsWith)]
    public async Task CanDeserializeStartsWithAndEndsWithOperators(string wireName, ComparisonOperator expected)
    {
        var json = $$"""
                   {
                       "key": "email",
                       "type": "person",
                       "value": "posthog",
                       "operator": "{{wireName}}"
                   }
                   """;
        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<Filter>(json);

        var propertyFilter = Assert.IsType<PropertyFilter>(result);
        Assert.Equal(expected, propertyFilter.Operator);
    }

    [Fact]
    public async Task DeserializesUnrecognizedOperatorAsUnknown()
    {
        var json = """
                   {
                       "key": "email",
                       "type": "person",
                       "value": "posthog",
                       "operator": "future_operator"
                   }
                   """;
        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<Filter>(json);

        var propertyFilter = Assert.IsType<PropertyFilter>(result);
        Assert.Equal(ComparisonOperator.Unknown, propertyFilter.Operator);
    }

    [Fact]
    public async Task CanDeserializeFilterGroup()
    {
        var json = """
                   {
                       "type": "OR",
                       "values": [
                           {
                               "type": "AND",
                               "values": [
                               {
                                   "key": "work_email",
                                   "operator": "is_set",
                                   "type": "person",
                                   "value": "is_set"
                               },
                               {
                                   "key": "home_email",
                                   "operator": "regex",
                                   "type": "person",
                                   "value": "^.*?@posthog.com$"
                                }
                            ]
                            },
                       {
                           "key": "email",
                           "type": "person",
                           "value": [
                               "tyrion@example.com",
                               "danaerys@example.com",
                               "sansa@example.com",
                               "ned@example.com"
                           ],
                           "operator": "exact"
                        }
                       ]
                   }
                   """;
        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<Filter>(json);

        var filterSet = Assert.IsType<FilterSet>(result);

        var expected = new FilterSet
        {
            Type = FilterType.Or,
            Values = new Filter[]
            {
                new FilterSet
                {
                    Type = FilterType.And,
                    Values = new Filter[]
                    {
                        new PropertyFilter
                        {
                            Type = FilterType.Person,
                            Key = "work_email",
                            Value = new PropertyFilterValue("is_set"),
                            Operator = ComparisonOperator.IsSet
                        },
                        new PropertyFilter
                        {
                            Type = FilterType.Person,
                            Key = "home_email",
                            Value = new PropertyFilterValue("^.*?@posthog.com$"),
                            Operator = ComparisonOperator.Regex
                        }
                    }
                },
                new PropertyFilter
                {
                    Type = FilterType.Person,
                    Key = "email",
                    Value = new PropertyFilterValue(new[]
                    {
                        "tyrion@example.com",
                        "danaerys@example.com",
                        "sansa@example.com",
                        "ned@example.com"
                    }),
                    Operator = ComparisonOperator.Exact
                }
            }
        };
        Assert.Equal(expected, filterSet);
    }
}