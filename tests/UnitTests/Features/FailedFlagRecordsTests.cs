#pragma warning disable CS0618 // Cover the deprecated direct getter and bulk fallback.
using System.Text.Json;
using PostHog;
using UnitTests.Fakes;

namespace FeatureFlagsTests;

public class FailedFlagRecordsTests
{
    [Theory]
    [InlineData("true", false, true, false)]
    [InlineData("false", false, true, false)]
    [InlineData(null, false, true, false)]
    [InlineData("false", false, true, true)]
    [InlineData(null, false, true, true)]
    [InlineData("true", true, true, false)]
    [InlineData("false", true, true, true)]
    [InlineData(null, true, false, true)]
    public async Task DirectFailedReadReturnsNullAndTracksFailure(string? globalError, bool localFallback, bool sendEvents, bool failedVariant)
    {
        var container = localFallback ? new TestContainer(personalApiKey: "secret") : new TestContainer();
        if (localFallback)
        {
            container.FakeHttpMessageHandler.AddLocalEvaluationResponse(LocalDefinitions);
        }
        container.FakeHttpMessageHandler.AddRepeatedFlagsResponse(2, Response(globalError, failedVariant));
        var batch = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var options = new FeatureFlagOptions { SendFeatureFlagEvents = sendEvents };

        Assert.Null(await client.GetFeatureFlagAsync("checkout", "person", options));
        Assert.Null(await client.GetFeatureFlagAsync("checkout", "person", options));
        await client.FlushAsync();

        if (!sendEvents)
        {
            Assert.Empty(batch.ReceivedRequests);
            return;
        }
        using var body = JsonDocument.Parse(batch.GetReceivedRequestBody(indented: false));
        var events = body.RootElement.GetProperty("batch");
        Assert.Equal(1, events.GetArrayLength());
        var properties = events[0].GetProperty("properties");
        Assert.False(properties.GetProperty("$feature_flag_response").GetBoolean());
        Assert.Equal(globalError == "true" ? "errors_while_computing_flags,evaluation_failed" : "evaluation_failed",
            properties.GetProperty("$feature_flag_error").GetString());
        Assert.Equal("request", properties.GetProperty("$feature_flag_request_id").GetString());
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData(null)]
    public async Task FailedFallbackPreservesSuccessfulLocalValuesAndPayloads(string? globalError)
    {
        var container = new TestContainer(personalApiKey: "secret");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(LocalDefinitions);
        container.FakeHttpMessageHandler.AddFlagsResponse(Response(globalError));
        var client = container.Activate<PostHogClient>();

        var flags = await client.GetAllFeatureFlagsAsync("person");

        Assert.Equal(4, flags.Count);
        Assert.True(flags["enabled"].IsEnabled);
        Assert.False(flags["disabled"].IsEnabled);
        Assert.Equal("blue", flags["variant"].VariantKey);
        foreach (var key in new[] { "enabled", "disabled", "variant" })
        {
            Assert.Equal("local", flags[key].Payload!.RootElement.GetProperty("copy").GetString());
        }
        Assert.False(flags["healthy"].IsEnabled);
        Assert.False(flags.ContainsKey("checkout"));
    }

    [Fact]
    public async Task HealthyFalseRemainsAResultAndDoesNotInheritSiblingFailure()
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddRepeatedFlagsResponse(2, Response("true"));
        var batch = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.GetFeatureFlagAsync("healthy", "person");

        Assert.NotNull(result);
        Assert.False(result.IsEnabled);
        var flags = await client.GetAllFeatureFlagsAsync("person");
        Assert.Single(flags);
        Assert.True(flags.ContainsKey("healthy"));
        await client.FlushAsync();
        Assert.DoesNotContain("evaluation_failed", batch.GetReceivedRequestBody(indented: false), StringComparison.Ordinal);
    }

    static string Response(string? globalError, bool failedVariant = true) => $$$"""
        {
          "requestId":"request",
          {{{(globalError is null ? "" : $"\"errorsWhileComputingFlags\":{globalError},")}}}
          "featureFlags":{"checkout":"leaked"},
          "featureFlagPayloads":{"checkout":"\"leaked\""},
          "flags":{
            "checkout":{"key":"checkout","enabled":false,"variant":{{{(failedVariant ? "\"leaked\"" : "null")}}},"failed":true,"metadata":{"payload":"{\"copy\":\"remote\"}"}},
            "enabled":{"key":"enabled","enabled":false,"failed":true,"metadata":{"payload":"{\"copy\":\"remote\"}"}},
            "disabled":{"key":"disabled","enabled":true,"failed":true,"metadata":{"payload":"{\"copy\":\"remote\"}"}},
            "variant":{"key":"variant","enabled":true,"variant":"remote","failed":true,"metadata":{"payload":"{\"copy\":\"remote\"}"}},
            "healthy":{"key":"healthy","enabled":false,"failed":false,"metadata":{}}
          }
        }
        """;

    const string LocalDefinitions = """
        {"flags":[
          {"id":1,"key":"enabled","active":true,"filters":{"groups":[{"properties":[],"rollout_percentage":100}],"payloads":{"true":"{\"copy\":\"local\"}"}}},
          {"id":2,"key":"disabled","active":true,"filters":{"groups":[{"properties":[],"rollout_percentage":0}],"payloads":{"false":"{\"copy\":\"local\"}"}}},
          {"id":3,"key":"variant","active":true,"filters":{"groups":[{"properties":[],"rollout_percentage":100}],"multivariate":{"variants":[{"key":"blue","rollout_percentage":100}]},"payloads":{"blue":"{\"copy\":\"local\"}"}}},
          {"id":4,"key":"checkout","active":true,"filters":{"groups":[{"properties":[{"key":"country","value":"US","type":"person"}],"rollout_percentage":100}]}}
        ]}
        """;
}
