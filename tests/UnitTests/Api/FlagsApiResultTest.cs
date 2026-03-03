using PostHog.Api;
using PostHog.Json;

namespace FlagsApiResultTests;

public class TheNormalizeResultMethod
{
    [Fact]
    public void HandlesV3Response()
    {
        var flagsApiResult = new FlagsApiResult
        {
            FeatureFlags = new Dictionary<string, StringOrValue<bool>>
            {
                { "boolean-flag", true },
                { "variant-flag", "some-variant" },
                { "disabled-flag", false },
                { "payload-flag", true}
            },
            FeatureFlagPayloads = new Dictionary<string, string>
            {
                { "boolean-flag", "[0, 1, 2]" },
                { "payload-flag", "{ \"key\": \"value\" }" }
            },
            ErrorsWhileComputingFlags = false,
            QuotaLimited = [],
            RequestId = "request-id"
        };

        var result = flagsApiResult.NormalizeResult();

        Assert.NotNull(result);

        Assert.Equal(new Dictionary<string, FeatureFlagResult>
        {
            ["boolean-flag"] = new()
            {
                Key = "boolean-flag",
                Enabled = true,
                Variant = null,
                Reason = new EvaluationReason
                {
                    Code = null,
                    Description = null,
                    ConditionIndex = null
                },
                Metadata = new FeatureFlagMetadata
                {
                    Id = null,
                    Version = null,
                    Payload = "[0, 1, 2]",
                    Description = null
                }
            },
            ["variant-flag"] = new FeatureFlagResult
            {
                Key = "variant-flag",
                Enabled = false,
                Variant = "some-variant",
                Reason = new EvaluationReason
                {
                    Code = null,
                    Description = null,
                    ConditionIndex = null
                },
                Metadata = new FeatureFlagMetadata
                {
                    Id = null,
                    Version = null,
                    Payload = null,
                    Description = null
                }
            },
            ["disabled-flag"] = new FeatureFlagResult
            {
                Key = "disabled-flag",
                Enabled = false,
                Variant = null,
                Reason = new EvaluationReason
                {
                    Code = null,
                    Description = null,
                    ConditionIndex = null
                },
                Metadata = new FeatureFlagMetadata
                {
                    Id = null,
                    Version = null,
                    Payload = null,
                    Description = null
                }
            },
            ["payload-flag"] = new FeatureFlagResult
            {
                Key = "payload-flag",
                Enabled = true,
                Variant = null,
                Reason = new EvaluationReason
                {
                    Code = null,
                    Description = null,
                    ConditionIndex = null
                },
                Metadata = new FeatureFlagMetadata
                {
                    Id = null,
                    Version = null,
                    Payload = "{ \"key\": \"value\" }",
                    Description = null
                }
            }
        }, result.Flags);
    }

    [Fact]
    public void HandlesV4Response()
    {
        var flagsApiResult = new FlagsApiResult
        {
            Flags = new Dictionary<string, FeatureFlagResult>
            {
                ["boolean-flag"] = new FeatureFlagResult
                {
                    Key = "boolean-flag",
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
                },
                ["variant-flag"] = new FeatureFlagResult
                {
                    Key = "variant-flag",
                    Enabled = false,
                    Variant = "some-variant",
                    Reason = new EvaluationReason
                    {
                        Code = "default",
                        Description = "Default reason",
                        ConditionIndex = 0
                    },
                    Metadata = new FeatureFlagMetadata
                    {
                        Id = 2,
                        Version = 1,
                        Payload = null,
                        Description = "Default metadata"
                    }
                },
                ["disabled-flag"] = new FeatureFlagResult
                {
                    Key = "disabled-flag",
                    Enabled = false,
                    Variant = null,
                    Reason = new EvaluationReason
                    {
                        Code = "no_condition_match",
                        Description = "Did not match any conditions",
                        ConditionIndex = 0
                    },
                    Metadata = new FeatureFlagMetadata
                    {
                        Id = 3,
                        Version = 23,
                        Payload = null,
                        Description = "This is an enabled flag"
                    }
                },
                ["payload-flag"] = new FeatureFlagResult
                {
                    Key = "payload-flag",
                    Enabled = true,
                    Variant = null,
                    Reason = new EvaluationReason
                    {
                        Code = "default",
                        Description = "Default reason",
                        ConditionIndex = 0
                    },
                    Metadata = new FeatureFlagMetadata
                    {
                        Id = 4,
                        Version = 12,
                        Payload = "{ \"key\": \"value\" }",
                        Description = "Default metadata"
                    }
                }
            },
            ErrorsWhileComputingFlags = false,
            QuotaLimited = [],
            RequestId = "request-id"
        };

        var result = flagsApiResult.NormalizeResult();

        Assert.NotNull(result);

        Assert.Equal(new Dictionary<string, StringOrValue<bool>>
        {
            ["boolean-flag"] = true,
            ["variant-flag"] = "some-variant",
            ["disabled-flag"] = false,
            ["payload-flag"] = true
        }, result.FeatureFlags);

        Assert.Equal(new Dictionary<string, string>
        {
            ["boolean-flag"] = "{\"foo\": 1}",
            ["payload-flag"] = "{ \"key\": \"value\" }"
        }, result.FeatureFlagPayloads);
    }
}
