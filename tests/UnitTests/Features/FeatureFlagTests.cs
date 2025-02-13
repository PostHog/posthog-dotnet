using System.Text.Json;
using PostHog.Api;
using PostHog.Features;
using PostHog.Json;

namespace FeatureFlagTests;

public class TheEqualsMethod
{
    [Fact]
    public void ReturnsTrueForEquivalentFlags()
    {
        var featureFlag1 = new FeatureFlag
        {
            Key = "key",
            IsEnabled = true,
            VariantKey = "variantKey",
            Payload = JsonDocument.Parse("""{"foo":  "bar"}""")
        };
        var featureFlag2 = new FeatureFlag
        {
            Key = "key",
            IsEnabled = true,
            VariantKey = "variantKey",
            Payload = JsonDocument.Parse("""{"foo":  "bar"}""")
        };

        var result = featureFlag1.Equals(featureFlag2);

        Assert.True(result);
    }

    [Fact]
    public void ReturnsFalseForUnequalFlags()
    {
        var featureFlag1 = new FeatureFlag
        {
            Key = "key",
            IsEnabled = true,
            VariantKey = "variantKey",
            Payload = JsonDocument.Parse("{}")
        };

        var featureFlag2 = new FeatureFlag
        {
            Key = "ke2y",
            IsEnabled = true,
            VariantKey = "variantKey",
            Payload = JsonDocument.Parse("{}")
        };

        var result = featureFlag1.Equals(featureFlag2);

        Assert.False(result);
    }
}

public class TheCreateFromDecideMethod
{
    [Fact]
    public void CanCreateFromApiResult()
    {
        var apiResult = new DecideApiResult
        {
            FeatureFlags = new Dictionary<string, StringOrValue<bool>>
            {
                ["some-key"] = new(true)
            },
            FeatureFlagPayloads = new Dictionary<string, string>
            {
                { "some-key", """{"foo": "bar"}""" }
            }
        };

        var flag = FeatureFlag.CreateFromDecide("some-key", true, apiResult);

        Assert.Equal("some-key", flag.Key);
        Assert.True(flag.IsEnabled);
        Assert.Null(flag.VariantKey);
        Assert.NotNull(flag.Payload);
        JsonAssert.Equal("""{"foo": "bar"}""", flag.Payload);
    }
}

public class TheCreateFromLocalEvaluationMethod
{
    [Fact]
    public void CanCreateFromLocalEvaluation()
    {
        var localFeatureFlag = new LocalFeatureFlag
        {
            Key = "some-key",
            Filters = new FeatureFlagFilters
            {
                Payloads = new Dictionary<string, string>
                {
                    ["true"] = """{"foo": "bar"}"""
                }
            }
        };

        var flag = FeatureFlag.CreateFromLocalEvaluation("some-key", new(true), localFeatureFlag);

        Assert.Equal("some-key", flag.Key);
        Assert.True(flag.IsEnabled);
        Assert.Null(flag.VariantKey);
        Assert.NotNull(flag.Payload);
        JsonAssert.Equal("""{"foo": "bar"}""", flag.Payload);
    }
}