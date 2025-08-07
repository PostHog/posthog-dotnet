using PostHog.Api;
using PostHog.Json;
using Xunit;

namespace JsonSerializerWrapperTests;

public class JsonSerializerWrapperTestSuite
{
  private readonly JsonSerializerWrapper _wrapper;
  private readonly SystemTextJsonSerializer _serializer;

  public JsonSerializerWrapperTestSuite()
  {
    _serializer = new SystemTextJsonSerializer();
    _wrapper = new JsonSerializerWrapper(_serializer);
  }

  public class TheSerializeToCamelCaseJsonMethod
  {
    private readonly JsonSerializerWrapper _wrapper;
    private readonly SystemTextJsonSerializer _serializer;

    public TheSerializeToCamelCaseJsonMethod()
    {
      _serializer = new SystemTextJsonSerializer();
      _wrapper = new JsonSerializerWrapper(_serializer);
    }

    [Fact]
    public async Task ShouldSerializeObjectToCamelCaseJson()
    {
      var obj = new { PropertyOne = "value", PropertyTwo = 1 };

      var json = await _wrapper.SerializeToCamelCaseJsonStringAsync(obj);

      Assert.Equal("{\"propertyOne\":\"value\",\"propertyTwo\":1}", json);
    }

    [Fact]
    public async Task ShouldSerializeWithCustomSerializer()
    {
      var customSerializer = new CustomTestSerializer();
      var wrapper = new JsonSerializerWrapper(customSerializer);
      var obj = new { PropertyOne = "value", PropertyTwo = 1 };

      var json = await wrapper.SerializeToCamelCaseJsonStringAsync(obj);

      Assert.Equal("{\"propertyOne\":\"value\",\"propertyTwo\":1}", json);
      Assert.True(customSerializer.SerializeCalled);
    }
  }

  public class TheDeserializeFromCamelCaseJsonMethod
  {
    private readonly JsonSerializerWrapper _wrapper;
    private readonly SystemTextJsonSerializer _serializer;

    public TheDeserializeFromCamelCaseJsonMethod()
    {
      _serializer = new SystemTextJsonSerializer();
      _wrapper = new JsonSerializerWrapper(_serializer);
    }

    [Fact]
    public async Task CanDeserializeJsonToDecideApiResult()
    {
      var json = await File.ReadAllTextAsync("./Fixtures/decide-api-result-v3.json");

      var result = await _wrapper.DeserializeFromCamelCaseJsonStringAsync<DecideApiResult>(json);

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
    public async Task CanDeserializeJsonToDecideApiV4Result()
    {
      var json = await File.ReadAllTextAsync("./Fixtures/decide-api-result-v4.json");

      var result = await _wrapper.DeserializeFromCamelCaseJsonStringAsync<DecideApiResult>(json);

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
    public async Task CanDeserializeNegatedJsonToDecideApiResult()
    {
      var json = await File.ReadAllTextAsync("./Fixtures/decide-api-result-v3-negated.json");

      var result = await _wrapper.DeserializeFromCamelCaseJsonStringAsync<DecideApiResult>(json);

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

      var result = await _wrapper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(json);

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
      var json = await File.ReadAllTextAsync("./Fixtures/local-evaluation-api-result-2.json");

      var result = await _wrapper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(json);

      Assert.NotNull(result);
    }

    [Fact]
    public async Task ShouldDeserializeApiResult()
    {
      var json = "{\"status\": 1}";

      var result = await _wrapper.DeserializeFromCamelCaseJsonStringAsync<ApiResult>(json);

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

      var result = await _wrapper.DeserializeFromCamelCaseJsonStringAsync<FeatureFlagResult>(json);

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

      var result = await _wrapper.DeserializeFromCamelCaseJsonStringAsync<Dictionary<string, FeatureFlagResult>>(json);

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

      var result = await _wrapper.DeserializeFromCamelCaseJsonStringAsync<PropertyFilter>(json);

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
          await _wrapper.DeserializeFromCamelCaseJsonStringAsync<Dictionary<string, object?>>(json);

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

      var result = await _wrapper.DeserializeFromCamelCaseJsonStringAsync<ClassWithStringOr>(json);

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

      var result = await _wrapper.DeserializeFromCamelCaseJsonStringAsync<ClassWithStringOr>(json);

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

      var result = await _wrapper.DeserializeFromCamelCaseJsonStringAsync<ClassWithStringOr>(json);

      Assert.NotNull(result);
      Assert.Equal("danaerys", result.TrueOrValue.StringValue);
      Assert.False(result.TrueOrValue.Value);
    }

    [Fact]
    public async Task ShouldUseCustomSerializer()
    {
      var customSerializer = new CustomTestSerializer();
      var wrapper = new JsonSerializerWrapper(customSerializer);
      var json = "{\"status\": 1}";

      var result = await wrapper.DeserializeFromCamelCaseJsonStringAsync<ApiResult>(json);

      Assert.NotNull(result);
      Assert.Equal(1, result.Status);
      Assert.True(customSerializer.DeserializeCalled);
    }

    public class ClassWithStringOr
    {
      public StringOrValue<bool> TrueOrValue { get; set; } = null!;
      public StringOrValue<bool> AnotherTrueOrValue { get; set; } = null!;
    }
  }

  public class TheDeserializeFromCamelCaseJsonStreamMethod
  {
    private readonly JsonSerializerWrapper _wrapper;
    private readonly SystemTextJsonSerializer _serializer;

    public TheDeserializeFromCamelCaseJsonStreamMethod()
    {
      _serializer = new SystemTextJsonSerializer();
      _wrapper = new JsonSerializerWrapper(_serializer);
    }

    [Fact]
    public async Task CanDeserializeFromStream()
    {
      var json = "{\"status\": 1}";
      using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

      var result = await _wrapper.DeserializeFromCamelCaseJsonAsync<ApiResult>(stream);

      Assert.NotNull(result);
      Assert.Equal(1, result.Status);
    }

    [Fact]
    public async Task CanDeserializeComplexObjectFromStream()
    {
      var json = await File.ReadAllTextAsync("./Fixtures/decide-api-result-v3.json");
      using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

      var result = await _wrapper.DeserializeFromCamelCaseJsonAsync<DecideApiResult>(stream);

      Assert.NotNull(result);
      Assert.Equal(new Dictionary<string, StringOrValue<bool>>()
      {
        ["hogtied_got_character"] = "danaerys",
        ["hogtied-homepage-user"] = true,
        ["hogtied-homepage-bonanza"] = true
      }, result.FeatureFlags);
    }

    [Fact]
    public async Task ShouldUseCustomSerializerForStream()
    {
      var customSerializer = new CustomTestSerializer();
      var wrapper = new JsonSerializerWrapper(customSerializer);
      var json = "{\"status\": 1}";
      using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

      var result = await wrapper.DeserializeFromCamelCaseJsonAsync<ApiResult>(stream);

      Assert.NotNull(result);
      Assert.Equal(1, result.Status);
      Assert.True(customSerializer.DeserializeCalled);
    }
  }

  private sealed class CustomTestSerializer : PostHogSerializer
  {
    public bool SerializeCalled { get; private set; }
    public bool DeserializeCalled { get; private set; }

    private readonly SystemTextJsonSerializer _fallback = new();

    public override string Serialize(object obj)
    {
      SerializeCalled = true;
      return _fallback.Serialize(obj);
    }

    public override T Deserialize<T>(string json)
    {
      DeserializeCalled = true;
      return _fallback.Deserialize<T>(json);
    }
  }
}