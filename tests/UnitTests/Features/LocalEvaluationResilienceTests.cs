#pragma warning disable CS0618 // Tests retain coverage of the deprecated single-flag API surface.
using System.Net;
using System.Text;
using PostHog;
using UnitTests.Fakes;
#if NETCOREAPP3_1
using TestLibrary.Fakes.Polyfills;
#endif

namespace LocalEvaluationResilienceTests;

public class TheFailureCooldown
{
    static readonly Uri LocalEvaluationUrl = FakeHttpMessageHandlerExtensions.LocalEvaluationUrl;

    [Fact]
    public async Task DoesNotRefetchDefinitionsOnEveryFlagCallAfterAFailedLoad()
    {
        var container = new TestContainer("fake-personal-api-key");

        // The definitions payload cannot be deserialized (required "flags" is missing), so the load
        // fails. Register one counting handler per flag call so a broken cooldown would refetch and
        // be counted here rather than fall through to an uncounted 404.
        var definitionsFetchCount = 0;
        for (var i = 0; i < 3; i++)
        {
            container.FakeHttpMessageHandler.AddResponse(
                LocalEvaluationUrl,
                HttpMethod.Get,
                () =>
                {
                    Interlocked.Increment(ref definitionsFetchCount);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    });
                });
        }

        // The flag calls fall back to the remote endpoint while no evaluator is loaded.
        container.FakeHttpMessageHandler.AddRepeatedFlagsResponse(5, """{"featureFlags": {"some-flag": true}}""");

        var client = container.Activate<PostHogClient>();

        // Three flag calls within one poll interval. Only the first should fetch the definitions.
        for (var i = 0; i < 3; i++)
        {
            await client.GetFeatureFlagAsync("some-flag", "distinct-id", null, CancellationToken.None);
        }

        Assert.Equal(1, definitionsFetchCount);
    }
}

public class TheTolerantPayloadParsing
{
    [Fact]
    public async Task DropsAMalformedFlagButStillEvaluatesTheGoodFlagsLocally()
    {
        var container = new TestContainer("fake-personal-api-key");

        // "bad-flag" has a rollout percentage of the wrong JSON type, so it alone fails to parse.
        var rawDefinitions =
            """
            {
                "flags": [
                    {
                        "id": 1,
                        "key": "bad-flag",
                        "active": true,
                        "filters": {
                            "groups": [
                                { "properties": [], "rollout_percentage": "not-a-number" }
                            ]
                        }
                    },
                    {
                        "id": 2,
                        "key": "good-flag",
                        "active": true,
                        "filters": {
                            "groups": [
                                { "properties": [], "rollout_percentage": 100 }
                            ]
                        }
                    }
                ]
            }
            """;
        container.FakeHttpMessageHandler.AddResponse(
            FakeHttpMessageHandlerExtensions.LocalEvaluationUrl,
            HttpMethod.Get,
            rawDefinitions);

        // Fails the test if the good flag falls back to the remote endpoint.
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"good-flag": false}}""");

        var client = container.Activate<PostHogClient>();

        var flag = await client.GetFeatureFlagAsync("good-flag", "distinct-id", null, CancellationToken.None);

        Assert.NotNull(flag);
        Assert.True(flag.IsEnabled);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }
}
