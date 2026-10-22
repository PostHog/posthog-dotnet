using PostHog.Api;
using PostHog.Json;
using PostHog.Library;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JsonSerializerHelperTests;

public class TheSerializeToCamelCaseJsonMethod
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SerializesTypedNonFiniteNumbersAsNull(bool writeIndented)
    {
        var obj = new
        {
            Doubles = new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.MinValue, 0.0 },
            Singles = new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue, 0.0f },
            Nested = new { Double = double.NaN, Single = float.PositiveInfinity },
            NullableDouble = (double?)double.NegativeInfinity,
            NullableSingle = (float?)float.NaN,
            Null = (double?)null
        };

        var json = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(obj, writeIndented);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var doubles = root.GetProperty("doubles");
        var singles = root.GetProperty("singles");
        Assert.Equal(6, doubles.GetArrayLength());
        Assert.Equal(6, singles.GetArrayLength());
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(JsonValueKind.Null, doubles[i].ValueKind);
            Assert.Equal(JsonValueKind.Null, singles[i].ValueKind);
        }
        Assert.Equal(double.MaxValue, doubles[3].GetDouble());
        Assert.Equal(double.MinValue, doubles[4].GetDouble());
        Assert.Equal(0.0, doubles[5].GetDouble());
        Assert.Equal(float.MaxValue, singles[3].GetSingle());
        Assert.Equal(float.MinValue, singles[4].GetSingle());
        Assert.Equal(0.0f, singles[5].GetSingle());
        JsonAssert.Equal("""{"double":null,"single":null}""", root.GetProperty("nested").GetRawText());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("nullableDouble").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("nullableSingle").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("null").ValueKind);
    }

    [Fact]
    public async Task StillSupportsFiniteNumericDictionaryKeys()
    {
        var doubles = new Dictionary<double, double> { [1.25] = 2.5 };
        var singles = new Dictionary<float, float> { [1.5f] = 3.5f };

        var doubleJson = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(doubles);
        var singleJson = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(singles);

        JsonAssert.Equal("""{"1.25":2.5}""", doubleJson);
        JsonAssert.Equal("""{"1.5":3.5}""", singleJson);
        Assert.Equal(doubles, await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<Dictionary<double, double>>(doubleJson));
        Assert.Equal(singles, await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<Dictionary<float, float>>(singleJson));
    }

    [Fact]
    public async Task StillDeserializesFiniteNumbers()
    {
        var doubles = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<double[]>("[1.25,-2.5,0]");
        var singles = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<float[]>("[1.25,-2.5,0]");

        Assert.Equal(new[] { 1.25, -2.5, 0.0 }, doubles);
        Assert.Equal(new[] { 1.25f, -2.5f, 0.0f }, singles);
    }

    [Theory]
    [InlineData("\"NaN\"")]
    [InlineData("\"Infinity\"")]
    [InlineData("\"-Infinity\"")]
    public async Task StillRejectsNamedFloatingPointStrings(string json)
    {
        await Assert.ThrowsAsync<JsonException>(() => JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<double>(json));
        await Assert.ThrowsAsync<JsonException>(() => JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<float>(json));
    }

    [Fact]
    public async Task StillRejectsUnsupportedValues()
    {
        var obj = new { Callback = (Action)(() => { }) };

        await Assert.ThrowsAsync<NotSupportedException>(() => JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(obj));
    }

    [Fact]
    public async Task PreservesNumberHandlingOnFiniteModelMembers()
    {
        var model = new StringNumberModel { Measurement = 1.25, Score = 2.5f };

        var json = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(model);

        JsonAssert.Equal("""{"measurement":"1.25","score":"2.5"}""", json);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task WritesAttributedNonFiniteNumbersAsNull(double value)
    {
        var model = new NumberHandlingModel
        {
            Measurement = value,
            Score = (float)value,
            Readings = [value, 1.25, null],
            Samples = [(float)value, 2.5f, null]
        };

        var json = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(model);

        JsonAssert.Equal("""{"measurement":null,"score":null,"readings":[null,"1.25",null],"samples":[null,2.5,null],"count":3}""", json);
    }

    [Fact]
    public async Task PreservesTypeAndCollectionNumberHandling()
    {
        var model = new NumberHandlingModel { Measurement = 1.25, Score = 2.5f, Readings = [3.5, null], Samples = [4.5f, null] };

        var json = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(model);
        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<NumberHandlingModel>(json);
        var listJson = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(new StringNumberList { 1.25, double.NaN, null });
        var list = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<StringNumberList>("""["1.25",null]""");
        var map = new StringNumberMap { ["finite"] = 1.25f, ["nonFinite"] = float.PositiveInfinity };
        var mapJson = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(map);
        var restoredMap = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<StringNumberMap>("""{"finite":"1.25","nothing":null}""");

        JsonAssert.Equal("""{"measurement":"1.25","score":"2.5","readings":["3.5",null],"samples":[4.5,null],"count":3}""", json);
        Assert.NotNull(result);
        Assert.Equal(model.Measurement, result.Measurement);
        Assert.Equal(model.Score, result.Score);
        Assert.Equal(model.Readings, result.Readings);
        Assert.Equal(model.Samples, result.Samples);
        JsonAssert.Equal("""["1.25",null,null]""", listJson);
        Assert.Equal(new double?[] { 1.25, null }, list);
        JsonAssert.Equal("""{"finite":"1.25","nonFinite":null}""", mapJson);
        Assert.Equal(new StringNumberMap { ["finite"] = 1.25f, ["nothing"] = null }, restoredMap);
    }

    [Theory]
    [InlineData(JsonNumberHandling.Strict, "[1.25,2.5]", "[1.25,2.5]")]
    [InlineData(JsonNumberHandling.WriteAsString, "[\"1.25\",\"2.5\"]", "[1.25,2.5]")]
    [InlineData(JsonNumberHandling.AllowReadingFromString, "[1.25,2.5]", "[\"1.25\",\"2.5\"]")]
    [InlineData(JsonNumberHandling.AllowNamedFloatingPointLiterals, "[1.25,2.5]", "[1.25,2.5]")]
    public void PreservesOptionsNumberHandling(JsonNumberHandling handling, string expected, string input)
    {
        var options = new JsonSerializerOptions(JsonSerializerHelper.Options) { NumberHandling = handling };

        JsonAssert.Equal(expected, JsonSerializer.Serialize(new[] { 1.25, 2.5 }, options));
        JsonAssert.Equal(expected, JsonSerializer.Serialize(new[] { 1.25f, 2.5f }, options));
        Assert.Equal(new[] { 1.25, 2.5 }, JsonSerializer.Deserialize<double[]>(input, options));
        Assert.Equal(new[] { 1.25f, 2.5f }, JsonSerializer.Deserialize<float[]>(input, options));
        JsonAssert.Equal("[null,null]", JsonSerializer.Serialize(new[] { double.NaN, double.PositiveInfinity }, options));
    }

    [Theory]
    [InlineData("NaN", double.NaN)]
    [InlineData("Infinity", double.PositiveInfinity)]
    [InlineData("-Infinity", double.NegativeInfinity)]
    public async Task PreservesExplicitNamedNumberReads(string namedValue, double expected)
    {
        var json = "{\"measurement\":\"" + namedValue + "\",\"score\":\"" + namedValue + "\"}";

        var model = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<NamedNumberModel>(json);
        var options = new JsonSerializerOptions(JsonSerializerHelper.Options) { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

        Assert.NotNull(model);
        Assert.Equal(expected, model.Measurement);
        Assert.Equal((float)expected, model.Score);
        Assert.Equal(expected, JsonSerializer.Deserialize<double>("\"" + namedValue + "\"", options));
        Assert.Equal((float)expected, JsonSerializer.Deserialize<float>("\"" + namedValue + "\"", options));
        JsonAssert.Equal("""{"measurement":null,"score":null}""", await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(model));
    }

    [Theory]
    [InlineData(JsonNumberHandling.Strict)]
    [InlineData(JsonNumberHandling.WriteAsString)]
    public void MemberNumberHandlingOverridesCollectionType(JsonNumberHandling optionsHandling)
    {
        var model = new MemberOverrideModel
        {
            Values = [1.25, double.NaN, null],
            Metrics = new StringNumberMap { ["finite"] = 2.5f, ["nonFinite"] = float.PositiveInfinity }
        };
        var options = new JsonSerializerOptions(JsonSerializerHelper.Options) { NumberHandling = optionsHandling };

        var json = JsonSerializer.Serialize(model, options);
        var result = JsonSerializer.Deserialize<MemberOverrideModel>("""{"values":[1.25,null],"metrics":{"finite":2.5}}""", options);

        JsonAssert.Equal("""{"values":[1.25,null,null],"metrics":{"finite":2.5,"nonFinite":null}}""", json);
        Assert.NotNull(result);
        Assert.Equal(new double?[] { 1.25, null }, result.Values);
        Assert.Equal(new StringNumberMap { ["finite"] = 2.5f }, result.Metrics);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MemberOverrideModel>("""{"values":["1.25"]}""", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MemberOverrideModel>("""{"metrics":{"finite":"2.5"}}""", options));
    }

    [Fact]
    public async Task PreservesPlatformNumberHandlingForObjectMembers()
    {
        foreach (var value in new object[]
        {
            1.25, 2.5f, new { Amount = 3.5, Count = 2 }, new object[] { 4.5, "NaN" },
            new { Values = new StringNumberList { 1.25 }, Metrics = new StringNumberMap { ["value"] = 2.5f } }
        })
        {
            var model = new ObjectNumberModel { Value = value };
            var expected = JsonSerializer.Serialize(model, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            var json = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(model);

            JsonAssert.Equal(expected, json);
        }
    }

    [Fact]
    public async Task PreservesExplicitMemberConverter()
    {
        var json = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(new CustomNumberModel());

        JsonAssert.Equal("""{"measurement":"custom"}""", json);
        var model = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<CustomNumberModel>(json);
        Assert.NotNull(model);
        Assert.Equal(1.25, model.Measurement);
    }

    public sealed class StringNumberModel
    {
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public double Measurement { get; set; }

        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public float Score { get; set; }
    }

#pragma warning disable CA1819, CA2227, CA1002 // Test models exercise array and mutable collection serialization.
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public sealed class NumberHandlingModel
    {
        public double? Measurement { get; set; }
        public float? Score { get; set; }
        public double?[] Readings { get; set; } = [];
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public List<float?> Samples { get; set; } = [];

        [JsonNumberHandling(JsonNumberHandling.Strict)]
        public double Count { get; set; } = 3;
    }

    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public sealed class MemberOverrideModel
    {
        [JsonNumberHandling(JsonNumberHandling.Strict)]
        public StringNumberList Values { get; set; } = [];
        public StringNumberMap Metrics { get; set; } = [];
    }
#pragma warning restore CA1819, CA2227, CA1002

    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public sealed class StringNumberList : List<double?>;

    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public sealed class StringNumberMap : Dictionary<string, float?>;

    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public sealed class NamedNumberModel
    {
        public double? Measurement { get; set; }
        public float? Score { get; set; }
    }

    public sealed class ObjectNumberModel
    {
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public object Value { get; set; } = 1.25;
    }

    public sealed class CustomNumberModel
    {
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        [JsonConverter(typeof(CustomNumberConverter))]
        public double Measurement { get; set; } = 1.25;
    }

    public sealed class CustomNumberConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => 1.25;
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteStringValue("custom");
    }

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
    public async Task CanDeserializeLegacyJsonToFlagsApiResult()
    {
        var json = await File.ReadAllTextAsync("./Fixtures/flags-api-result-legacy.json");

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<FlagsApiResult>(json);

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
    public async Task CanDeserializeJsonToFlagsApiResult()
    {
        var json = await File.ReadAllTextAsync("./Fixtures/flags-api-result.json");

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<FlagsApiResult>(json);

        Assert.NotNull(result);
        Assert.Equal(
            new FeatureFlagResult
            {
                Key = "multi-variate-flag",
                Enabled = true,
                Variant = "hello",
                Reason = new EvaluationReason
                {
                    Code = "condition_match",
                    Description = "Matched conditions set 2",
                    ConditionIndex = 1
                },
                Metadata = new FeatureFlagMetadata
                {
                    Id = 4,
                    Version = 42,
                    Payload = "this is the payload"
                }
            },
            result.Flags?["multi-variate-flag"]
        );
        Assert.Equal(
            new FeatureFlagResult
            {
                Key = "false-flag-2",
                Enabled = false,
                Variant = null,
                Reason = new EvaluationReason
                {
                    Code = "no_condition_match",
                    Description = "No matching condition set",
                    ConditionIndex = null
                },
                Metadata = new FeatureFlagMetadata
                {
                    Id = 10,
                    Version = 1,
                    Payload = null
                }
            },
            result.Flags?["false-flag-2"]
        );
    }

    [Fact]
    public async Task CanDeserializeNegatedJsonToFlagsApiResult()
    {
        var json = await File.ReadAllTextAsync("./Fixtures/flags-api-result-legacy-negated.json");

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<FlagsApiResult>(json);

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
        var json = await File.ReadAllTextAsync("./Fixtures/local-evaluation-api-result.json");

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(json);

        var expected = new LocalEvaluationApiResult
        {
            Flags = new LocalFeatureFlag[]
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
                                }.ToReadOnlyList()
                            }
                        }.ToReadOnlyList(),
                        Payloads = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
                        {
                            ["cersei"] = "{\"role\": \"burn it all down\"}",
                            ["tyrion"] = "{\"role\": \"advisor\"}",
                            ["danaerys"] = "{\"role\": \"khaleesi\"}",
                            ["jon-snow"] = "{\"role\": \"king in the north\"}"
                        }),
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
                                }.ToReadOnlyList(),
                                RolloutPercentage = 80
                            }
                        }.ToReadOnlyList(),
                        Payloads = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
                        {
                            ["true"] = "{\"is_cool\": true}"
                        })
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
                                }.ToReadOnlyList()
                            }
                        }.ToReadOnlyList()
                    },
                    Deleted = false,
                    Active = false,
                    EnsureExperienceContinuity = false
                },
                new LocalFeatureFlag
                {
                    Id = 2,
                    TeamId = 42,
                    Name = "Advanced file previews",
                    Key = "advanced-file-previews",
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
                                        Key = "file-previews",
                                        Type = FilterType.Flag,
                                        Value = new PropertyFilterValue(true),
                                        DependencyChain = new[] { "file-previews" }.ToReadOnlyList(),
                                        Operator = ComparisonOperator.Exact
                                    }
                                }.ToReadOnlyList(),
                                RolloutPercentage = 100
                            }
                        }.ToReadOnlyList()
                    },
                    Deleted = false,
                    Active = true,
                    EnsureExperienceContinuity = false
                }
            }.ToReadOnlyList(),
            GroupTypeMapping = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
            {
                ["0"] = "account",
                ["1"] = "instance",
                ["2"] = "organization",
                ["3"] = "project",
                ["4"] = "company"
            }),
            Cohorts = new ReadOnlyDictionary<string, FilterSet>(new Dictionary<string, FilterSet>
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
                            }.ToReadOnlyList()
                        }
                    }.ToReadOnlyList()
                }
            })
        };

        Assert.Equal(expected, result);
        Assert.NotNull(result);
        Assert.Equal(4, result.Flags.Count);

        // Check that the flag with dependency is present and correctly deserialized
        var dependentFlag = result.Flags.FirstOrDefault(f => f.Key == "advanced-file-previews");
        Assert.NotNull(dependentFlag);
        Assert.True(dependentFlag.Filters?.Groups?.Count > 0);
        Assert.True(dependentFlag.Filters?.Groups?[0].Properties?.Count > 0);
        var property = dependentFlag.Filters?.Groups?[0].Properties?[0];
        Assert.Equal(FilterType.Flag, property?.Type);
        Assert.Equal(new[] { "file-previews" }, property?.DependencyChain);

        // Verify other expected flags are present
        Assert.Contains(result.Flags, f => f.Key == "hogtied_got_character");
        Assert.Contains(result.Flags, f => f.Key == "hogtied-homepage-user");
        Assert.Contains(result.Flags, f => f.Key == "file-previews");

        // Verify group type mapping and cohorts are present
        Assert.NotNull(result.GroupTypeMapping);
        Assert.NotNull(result.Cohorts);
    }

    [Fact]
    public async Task CanDeserializeAnotherLocalEvaluationApiResult()
    {
        var json = await File.ReadAllTextAsync("./Fixtures/local-evaluation-api-result-2.json");

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(json);

        Assert.NotNull(result);
        Assert.Equal(5, result.Flags.Count);
        var survey = result.Flags[0];
        Assert.Equal("survey-targeting-0da71edbdd-custom", survey.Key);
        Assert.False(survey.Active);
        var surveyGroup = Assert.Single(survey.Filters!.Groups!);
        Assert.Equal("", surveyGroup.Variant);
        Assert.Equal(2, surveyGroup.Properties!.Count);
        Assert.All(surveyGroup.Properties, property => Assert.Equal(ComparisonOperator.IsNotSet, property.Operator));

        Assert.Equal("{\"not\": \"a\", \"secret\": {\"payload\": 42}}", result.Flags[1].Filters!.Payloads!["true"]);
        var multivariate = result.Flags[2].Filters!;
        Assert.Equal(50, Assert.Single(multivariate.Groups!).RolloutPercentage);
        Assert.Equal(["first-variant-key", "second-variant-key", "third-variant-key", "fourth-variant"],
            multivariate.Multivariate!.Variants.Select(variant => variant.Key));
        Assert.All(multivariate.Multivariate.Variants, variant => Assert.Equal(25, variant.RolloutPercentage));
        Assert.Equal("42", multivariate.Payloads!["first-variant-key"]);
        Assert.Equal("\"This is a test payload\"", result.Flags[3].Filters!.Payloads!["true"]);
        Assert.Equal(3, result.Flags[4].Filters!.AggregationGroupTypeIndex);
        Assert.Equal("project", result.GroupTypeMapping!["3"]);
        var cohort = Assert.IsType<FilterSet>(Assert.Single(result.Cohorts!["1"].Values));
        Assert.Equal(ComparisonOperator.IsSet, Assert.IsType<PropertyFilter>(Assert.Single(cohort.Values)).Operator);
    }

    [Fact]
    public async Task CanDeserializeLocalEvaluationFiltersWithMissingAndNumericValues()
    {
        var json = """
                   {
                     "flags": [
                       {
                         "id": 1,
                         "team_id": 1,
                         "key": "example-flag",
                         "active": true,
                         "deleted": false,
                         "filters": {
                           "groups": [
                             {
                               "properties": [
                                 { "key": "email", "operator": "is_set", "type": "person" },
                                 { "key": "last_seen", "operator": "is_not_set", "type": "person" },
                                 { "key": "orders_count", "operator": "exact", "type": "person", "value": [0, 3.14] }
                               ],
                               "rollout_percentage": 100
                             }
                           ]
                         }
                       }
                     ],
                     "group_type_mapping": {},
                     "cohorts": {}
                   }
                   """;

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(json);

        Assert.NotNull(result);
        var properties = Assert.Single(Assert.Single(result.Flags).Filters!.Groups!).Properties!;
        Assert.Equal(new PropertyFilter
        {
            Type = FilterType.Person,
            Key = "email",
            Operator = ComparisonOperator.IsSet
        }, properties[0]);
        Assert.Equal(new PropertyFilter
        {
            Type = FilterType.Person,
            Key = "last_seen",
            Operator = ComparisonOperator.IsNotSet
        }, properties[1]);
        Assert.Equal(["0", "3.14"], properties[2].Value!.ListOfStrings);
    }
    [Fact]
    public async Task CanDeserializeLocalEvaluationApiResultWithUnrecognizedOperator()
    {
        var json = """
                   {
                       "flags": [
                           {
                               "id": 1,
                               "team_id": 2,
                               "name": "A flag using an operator this SDK doesn't know yet",
                               "key": "future-flag",
                               "filters": {
                                   "groups": [
                                       {
                                           "properties": [
                                               {
                                                   "key": "email",
                                                   "type": "person",
                                                   "value": "posthog",
                                                   "operator": "future_operator"
                                               }
                                           ]
                                       }
                                   ]
                               }
                           },
                           {
                               "id": 2,
                               "team_id": 2,
                               "name": "A flag using a known operator",
                               "key": "known-flag",
                               "filters": {
                                   "groups": [
                                       {
                                           "properties": [
                                               {
                                                   "key": "email",
                                                   "type": "person",
                                                   "value": "posthog",
                                                   "operator": "icontains"
                                               }
                                           ]
                                       }
                                   ]
                               }
                           }
                       ]
                   }
                   """;

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(json);

        Assert.NotNull(result);
        Assert.Equal(2, result.Flags.Count);
        Assert.Equal(
            ComparisonOperator.Unknown,
            result.Flags[0].Filters?.Groups?[0].Properties?[0].Operator);
        Assert.Equal(
            ComparisonOperator.ContainsIgnoreCase,
            result.Flags[1].Filters?.Groups?[0].Properties?[0].Operator);
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
    public async Task CanDeserializeFeatureFlagResult()
    {
        var json = """
                   {
                     "key": "enabled-flag",
                     "enabled": true,
                     "variant": null,
                     "reason": {
                       "code": "condition_match",
                       "description": "Matched conditions set 3",
                       "condition_index": 2
                     },
                     "metadata": {
                       "id": 1,
                       "version": 23,
                       "payload": "{\"foo\": 1}",
                       "description": "This is an enabled flag"  
                     }
                   }
                   """;

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<FeatureFlagResult>(json);

        Assert.NotNull(result);
        Assert.Equal(new FeatureFlagResult
        {
            Key = "enabled-flag",
            Enabled = true,
            Variant = null,
            Reason = new EvaluationReason
            {
                Code = "condition_match",
                Description = "Matched conditions set 3",
                ConditionIndex = 2
            },
            Metadata = new FeatureFlagMetadata
            {
                Id = 1,
                Version = 23,
                Payload = "{\"foo\": 1}",
                Description = "This is an enabled flag"
            }
        }, result);
    }

    [Fact]
    public async Task CanDeserializeDictionaryOfFeatureFlagResult()
    {
        var json = """
                   {
                       "enabled-flag": {
                         "key": "enabled-flag",
                         "enabled": true,
                         "variant": null,
                         "reason": {
                           "code": "condition_match",
                           "description": "Matched conditions set 3",
                           "condition_index": 2
                         },
                         "metadata": {
                           "id": 1,
                           "version": 23,
                           "payload": "{\"foo\": 1}",
                           "description": "This is an enabled flag"  
                         }
                       }
                   }
                   """;

        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<Dictionary<string, FeatureFlagResult>>(json);

        Assert.NotNull(result);
        Assert.Equal(new FeatureFlagResult
        {
            Key = "enabled-flag",
            Enabled = true,
            Variant = null,
            Reason = new EvaluationReason
            {
                Code = "condition_match",
                Description = "Matched conditions set 3",
                ConditionIndex = 2
            },
            Metadata = new FeatureFlagMetadata
            {
                Id = 1,
                Version = 23,
                Payload = "{\"foo\": 1}",
                Description = "This is an enabled flag"
            }
        }, result["enabled-flag"]);
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
        Assert.False(result.AnotherTrueOrValue.Value);
        Assert.True(result.AnotherTrueOrValue.IsValue);
    }

    public class ClassWithStringOr
    {
        public StringOrValue<bool> TrueOrValue { get; set; } = null!;
        public StringOrValue<bool> AnotherTrueOrValue { get; set; } = null!;
    }
}