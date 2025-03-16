using PostHog.Api;
using PostHog.Json;

namespace JsonSerializerHelperTests;

public class TheSerializeToCamelCaseJsonMethod
{
    [Fact]
    public async Task ShouldSerializeObjectToCamelCaseJson()
    {
        var obj = new { PropertyOne = "value", PropertyTwo = 1 };

        var json = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(obj);

        Assert.Equal("{\"propertyOne\":\"value\",\"propertyTwo\":1}", json);
    }
}

public class TheDeserializeFromCamelCaseJsonMethod
{
    [Fact]
    public async Task CanDeserializeJsonToDecideApiResult()
    {
        var json = await File.ReadAllTextAsync("./Json/decide-api-result-v3.json");

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<DecideApiResult>(json);

        Assert.NotNull(result);
        Assert.Equal(new Dictionary<string, StringOrValue<bool>>()
        {
            ["hogtied_got_character"] = "danaerys",
            ["hogtied-homepage-user"] = true,
            ["hogtied-homepage-bonanza"] = true
        }, result.FeatureFlags);
        Assert.False(result.ErrorsWhileComputingFlags);
        Assert.Equal(new Dictionary<string, string>
        {
            ["hogtied_got_character"] = "{\"role\": \"khaleesi\"}",
            ["hogtied-homepage-user"] = "{\"is_cool\": true}"
        }, result.FeatureFlagPayloads);
    }

    [Fact]
    public async Task CanDeserializeNegatedJsonToDecideApiResult()
    {
        var json = await File.ReadAllTextAsync("./Json/decide-api-result-v3-negated.json");

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<DecideApiResult>(json);

        Assert.NotNull(result);
        Assert.NotNull(result.FeatureFlagPayloads);
        Assert.Equal(new Dictionary<string, StringOrValue<bool>>()
        {
            ["hogtied_got_character"] = false,
            ["hogtied-homepage-user"] = false,
            ["hogtied-homepage-bonanza"] = false
        }, result.FeatureFlags);
        Assert.True(result.ErrorsWhileComputingFlags);
        Assert.Empty(result.FeatureFlagPayloads);
    }

    [Fact]
    public async Task CanDeserializeLocalEvaluationApiResult()
    {
        var json = await File.ReadAllTextAsync("./Json/local-evaluation-api-result.json");

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(json);

        var expected = new LocalEvaluationApiResult
        {
            Flags = new[]
            {
                new LocalFeatureFlag
                {
                    Id = 91866,
                    TeamId = 110510,
                    Name = "A multivariate feature flag that tells you what character you are",
                    Key = "hogtied_got_character",
                    Filters = new FeatureFlagFilters
                    {
                        Groups = new[]
                        {
                            new FeatureFlagGroup
                            {
                                Properties = new[]
                                {
                                    new PropertyFilter
                                    {
                                        Type = FilterType.Group,
                                        Key = "size",
                                        Value = new PropertyFilterValue(new[] { "small" }),
                                        Operator = ComparisonOperator.Exact,
                                        GroupTypeIndex = 3
                                    },
                                    new PropertyFilter
                                    {
                                        Type = FilterType.Cohort,
                                        Key = "id",
                                        Value = new PropertyFilterValue(1),
                                        Operator = ComparisonOperator.In
                                    },
                                    new PropertyFilter
                                    {
                                        Type = FilterType.Group,
                                        Key = "$group_key",
                                        Value = new PropertyFilterValue("12345"),
                                        Operator = ComparisonOperator.Exact,
                                        GroupTypeIndex = 3
                                    }
                                }
                            }
                        },
                        Payloads = new Dictionary<string, string>
                        {
                            ["cersei"] = "{\"role\": \"burn it all down\"}",
                            ["tyrion"] = "{\"role\": \"advisor\"}",
                            ["danaerys"] = "{\"role\": \"khaleesi\"}",
                            ["jon-snow"] = "{\"role\": \"king in the north\"}"
                        },
                        Multivariate = new Multivariate
                        {
                            Variants = new[]
                            {
                                new Variant
                                {
                                    Key = "tyrion",
                                    Name = "The one who talks",
                                    RolloutPercentage = 25
                                },
                                new Variant
                                {
                                    Key = "danaerys",
                                    Name = "The mother of dragons",
                                    RolloutPercentage = 25
                                },
                                new Variant
                                {
                                    Key = "jon-snow",
                                    Name = "Knows nothing",
                                    RolloutPercentage = 25
                                },
                                new Variant
                                {
                                    Key = "cersei",
                                    Name = "Not nice",
                                    RolloutPercentage = 25
                                }
                            }
                        }
                    },
                    Deleted = false,
                    Active = true,
                    EnsureExperienceContinuity = false
                },
                new LocalFeatureFlag
                {
                    Id = 91468,
                    TeamId = 110510,
                    Name = "Testing a PostHog client",
                    Key = "hogtied-homepage-user",
                    Filters = new FeatureFlagFilters
                    {
                        Groups = new[]
                        {
                            new FeatureFlagGroup
                            {
                                Variant = null,
                                Properties = new[]
                                {
                                    new PropertyFilter
                                    {
                                        Key = "$group_key",
                                        Type = FilterType.Group,
                                        Value = new PropertyFilterValue("01943db3-83be-0000-e7ea-ecae4d9b5afb"),
                                        Operator = ComparisonOperator.Exact,
                                        GroupTypeIndex = 2
                                    }
                                },
                                RolloutPercentage = 80
                            }
                        },
                        Payloads = new Dictionary<string, string>
                        {
                            ["true"] = "{\"is_cool\": true}"
                        }
                    },
                    Deleted = false,
                    Active = true,
                    EnsureExperienceContinuity = true
                },
                new LocalFeatureFlag
                {
                    Id = 1,
                    TeamId = 42,
                    Name = "File previews",
                    Key = "file-previews",
                    Filters = new FeatureFlagFilters
                    {
                        Groups = new[]
                        {
                            new FeatureFlagGroup
                            {
                                Properties = new[]
                                {
                                    new PropertyFilter
                                    {
                                        Key = "email",
                                        Type = FilterType.Person,
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
                            }
                        }
                    },
                    Deleted = false,
                    Active = false,
                    EnsureExperienceContinuity = false
                }
            },
            GroupTypeMapping = new Dictionary<string, string>
            {
                ["0"] = "account",
                ["1"] = "instance",
                ["2"] = "organization",
                ["3"] = "project",
                ["4"] = "company"
            },
            Cohorts = new Dictionary<string, FilterSet>
            {
                ["1"] = new FilterSet
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
                                }
                            }
                        }
                    }
                }
            }
        };

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task CanDeserializeAnotherLocalEvaluationApiResult()
    {
        var json = await File.ReadAllTextAsync("./Json/local-evaluation-api-result-2.json");

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(json);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ShouldDeserializeApiResult()
    {
        var json = "{\"status\": 1}";

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<ApiResult>(json);

        Assert.NotNull(result);
        Assert.Equal(1, result.Status);
    }

    [Fact]
    public async Task CanDeserializeFilterProperty()
    {
        var json = """
                   {
                     "key": "$group_key",
                     "type": "group",
                     "value": "01943db3-83be-0000-e7ea-ecae4d9b5afb",
                     "operator": "exact",
                     "group_type_index": 2
                   }
                   """;

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<PropertyFilter>(json);

        Assert.Equal(new PropertyFilter
        {
            Type = FilterType.Group,
            Key = "$group_key",
            Value = new PropertyFilterValue("01943db3-83be-0000-e7ea-ecae4d9b5afb"),
            Operator = ComparisonOperator.Exact,
            GroupTypeIndex = 2
        }, result);
    }

    [Fact]
    public async Task CanDeserializePropertiesDictionaryWithNullValue()
    {
        var json = """
                   {
                     "size": "large",
                     "email": null
                   }
                   """;

        var result =
            await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<Dictionary<string, object?>>(json);

        Assert.NotNull(result);
        Assert.Equal("large", result["size"]?.ToString());
        Assert.Null(result["email"]);
    }

    [Fact]
    public async Task CanDeserializeStringOrBool()
    {
        var json = """
                   {
                     "TrueOrValue" : "danaerys",
                     "AnotherTrueOrValue" : true
                   }
                   """;

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<ClassWithStringOr>(json);

        Assert.NotNull(result);
        Assert.Equal("danaerys", result.TrueOrValue.StringValue);
        Assert.True(result.AnotherTrueOrValue.Value);
    }

    [Fact]
    public async Task CanDeserializeCamelCasedStringOrBool()
    {
        var json = """
                   {
                     "trueOrValue" : "danaerys",
                     "anotherTrueOrValue" : true
                   }
                   """;

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<ClassWithStringOr>(json);

        Assert.NotNull(result);
        Assert.Equal("danaerys", result.TrueOrValue.StringValue);
        Assert.True(result.AnotherTrueOrValue.Value);
    }

    [Fact]
    public async Task CanDeserializeStringOrBoolWithFalse()
    {
        var json = """
                   {
                     "TrueOrValue": "danaerys",
                     "AnotherTrueOrValue": false
                   }
                   """;

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<ClassWithStringOr>(json);

        Assert.NotNull(result);
        Assert.Equal("danaerys", result.TrueOrValue.StringValue);
        Assert.False(result.TrueOrValue.Value);
    }

    public class ClassWithStringOr
    {
        public StringOrValue<bool> TrueOrValue { get; set; } = null!;
        public StringOrValue<bool> AnotherTrueOrValue { get; set; } = null!;
    }
}