using PostHog.Api;
using PostHog.Json;

public class JsonSerializerHelperTests
{
    public class TheSerializeToCamelCaseJsonMethod
    {
        [Fact]
        public async Task ShouldSerializeObjectToCamelCaseJson()
        {
            // Arrange
            var obj = new { PropertyOne = "value", PropertyTwo = 1 };

            // Act
            var json = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(obj);

            // Assert
            Assert.Equal("{\"propertyOne\":\"value\",\"propertyTwo\":1}", json);
        }
    }

    public class TheDeserializeFromCamelCaseJsonMethod
    {
        [Fact]
        public async Task ShouldDeserializeFeatureFlagsJsonToFeatureFlagResult()
        {
            // Arrange
            var json = await File.ReadAllTextAsync("./Json/feature-flags-v3.json");

            // Act

            var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<FeatureFlagsApiResult>(json);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Config.EnableCollectEverything);
            Assert.False(result.IsAuthenticated);
            Assert.Equal(new Dictionary<string, StringOrValue<bool>>()
            {
                ["hogtied_got_character"] = "danaerys",
                ["hogtied-homepage-user"] = true,
                ["hogtied-homepage-bonanza"] = true
            }, result.FeatureFlags);
            Assert.Equal("/i/v0/e/", result.Analytics.Endpoint);
            Assert.True(result.DefaultIdentifiedOnly);
            Assert.False(result.ErrorsWhileComputingFlags);
            Assert.Equal(new Dictionary<string, string>
            {
                ["hogtied_got_character"] = "{\"role\": \"khaleesi\"}",
                ["hogtied-homepage-user"] = "{\"is_cool\": true}"
            }, result.FeatureFlagPayloads);
        }

        [Fact]
        public async Task ShouldDeserializeFeatureFlagsNegatedJsonToFeatureFlagResult()
        {
            // Arrange
            var json = await File.ReadAllTextAsync("./Json/feature-flags-v3-negated.json");

            // Act

            var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<FeatureFlagsApiResult>(json);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.Config.EnableCollectEverything);
            Assert.True(result.IsAuthenticated);
            Assert.Equal(new Dictionary<string, StringOrValue<bool>>()
            {
                ["hogtied_got_character"] = false,
                ["hogtied-homepage-user"] = false,
                ["hogtied-homepage-bonanza"] = false
            }, result.FeatureFlags);
            Assert.Equal("/i/v0/e/", result.Analytics.Endpoint);
            Assert.False(result.DefaultIdentifiedOnly);
            Assert.True(result.ErrorsWhileComputingFlags);
            Assert.Empty(result.FeatureFlagPayloads);
        }

        [Fact]
        public async Task ShouldDeserializeApiResult()
        {
            // Arrange
            var json = "{\"status\": 1}";

            // Act
            var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<ApiResult>(json);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.Status);
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
}