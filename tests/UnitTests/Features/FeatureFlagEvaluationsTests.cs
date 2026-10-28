using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PostHog;
using PostHog.Features;
using UnitTests.Fakes;

namespace FeatureFlagEvaluationsTests;

public class TheEvaluateFlagsAsyncMethod
{
    static void AddResolvableLocalFlag(TestContainer container) =>
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {"flags": [
                {"id": 1, "key": "local-flag", "active": true,
                 "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}}
            ]}
            """);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullOptionsAndOmittedFlagKeysReturnSnapshotFromOneFlagsRequest(bool passOptions)
    {
        var container = new TestContainer();
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """
            {
                "featureFlags": {"flag-a": true, "flag-b": "variant-x"},
                "featureFlagPayloads": {"flag-a": "{\"hello\":\"world\"}"},
                "flags": {
                    "flag-a": {
                        "key": "flag-a",
                        "enabled": true,
                        "metadata": {"id": 42, "version": 7, "payload": "{\"hello\":\"world\"}"},
                        "reason": {"description": "matched condition set 1"}
                    },
                    "flag-b": {
                        "key": "flag-b",
                        "enabled": true,
                        "variant": "variant-x",
                        "metadata": {"id": 43, "version": 2},
                        "reason": {"description": "variant assignment"}
                    }
                },
                "requestId": "the-request-id",
                "evaluatedAt": 1705862903000
            }
            """);
        var client = container.Activate<PostHogClient>();

        var extraRequest = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        var options = passOptions ? new AllFeatureFlagsOptions() : null;
        var snapshot = await client.EvaluateFlagsAsync("user-1", options, CancellationToken.None);

        Assert.Equal(["flag-a", "flag-b"], snapshot.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.True(snapshot.IsEnabled("flag-a"));
        Assert.Equal("variant-x", snapshot.GetFlag("flag-b")?.VariantKey);
        Assert.Equal("world", snapshot.GetFlagPayload("flag-a")?.RootElement.GetProperty("hello").GetString());
        Assert.Empty(extraRequest.ReceivedRequests);
        Assert.Equal("the-request-id", snapshot.RequestId);
        Assert.Equal(1705862903000, snapshot.EvaluatedAt);
        Assert.Single(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task EmptyDistinctIdReturnsEmptySnapshotWithNoHttpCall()
    {
        var container = new TestContainer();
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"flag-a": true}}""");
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(string.Empty, options: null, CancellationToken.None);

        Assert.Empty(snapshot.Keys);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task ExplicitEmptyFlagKeysReturnEmptySnapshotWithoutEvaluationWork()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        var localHandler = container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """{"flags": [{"id": 1, "key": "local-flag", "active": true}]}""");
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"remote-flag": true}}""");
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var cache = Substitute.For<IFeatureFlagCache>();
        var client = container.Activate<PostHogClient>(cache);

        var snapshot = await client.EvaluateFlagsAsync(
            "user-1",
            new AllFeatureFlagsOptions
            {
                FlagKeysToEvaluate = [],
                Groups = new GroupCollection { { "company", "acme" } }
            },
            CancellationToken.None);

        Assert.Empty(snapshot.Keys);
        Assert.Null(snapshot.RequestId);
        Assert.Null(snapshot.EvaluatedAt);
        Assert.False(snapshot.IsEnabled("missing-flag"));
        await client.FlushAsync();

        using var doc = JsonDocument.Parse(batchHandler.GetReceivedRequestBody(indented: false));
        var properties = doc.RootElement.GetProperty("batch").EnumerateArray().Single().GetProperty("properties");
        Assert.Equal("acme", properties.GetProperty("$groups").GetProperty("company").GetString());
        Assert.Empty(cache.ReceivedCalls());
        Assert.Empty(localHandler.ReceivedRequests);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task ForwardsFlagKeysToFlagsRequestBody()
    {
        var container = new TestContainer();
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"flag-a": true}}""");
        var client = container.Activate<PostHogClient>();

        await client.EvaluateFlagsAsync(
            "user-1",
            new AllFeatureFlagsOptions { FlagKeysToEvaluate = ["flag-a", "flag-b"] },
            CancellationToken.None);

        var request = flagsHandler.ReceivedRequests.Single();
        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var flagKeys = doc.RootElement.GetProperty("flag_keys_to_evaluate")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToArray();
        Assert.Equal(new[] { "flag-a", "flag-b" }, flagKeys);
    }

    public static IEnumerable<object?[]> EvaluationContextsRequestBodyCases()
    {
        yield return [new[] { "production", "backend" }, new[] { "production", "backend" }];
        yield return [Array.Empty<string>(), null];
        yield return [null, null];
    }

    [Theory]
    [MemberData(nameof(EvaluationContextsRequestBodyCases))]
    public async Task SendsEvaluationContextsToFlagsRequestBodyWhenConfigured(
        string[]? configuredEvaluationContexts,
        string[]? expectedEvaluationContexts)
    {
        var container = new TestContainer(services => services.Configure<PostHogOptions>(options =>
        {
            options.EvaluationContexts = configuredEvaluationContexts;
        }));
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"flag-a": true}}""");
        var client = container.Activate<PostHogClient>();

        await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);

        var request = flagsHandler.ReceivedRequests.Single();
        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        if (expectedEvaluationContexts is null)
        {
            Assert.False(doc.RootElement.TryGetProperty("evaluation_contexts", out _));
        }
        else
        {
            var evaluationContexts = doc.RootElement.GetProperty("evaluation_contexts")
                .EnumerateArray()
                .Select(e => e.GetString() ?? string.Empty)
                .ToArray();
            Assert.Equal(expectedEvaluationContexts, evaluationContexts);
        }
    }

    [Fact]
    public async Task OnlyEvaluateLocallyDoesNotHitRemote()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {"flags": [{"id": 1, "key": "flag-a", "active": true, "rollout_percentage": 100, "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}}]}
            """);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user-1",
            new AllFeatureFlagsOptions
            {
                OnlyEvaluateLocally = true,
                FlagKeysToEvaluate = ["flag-a", "missing-flag"]
            },
            CancellationToken.None);

        Assert.True(snapshot.IsEnabled("flag-a"));
        Assert.Single(snapshot.Keys);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task MissingRequestedKeyFallsBackWithOriginalScopeAndKeepsLocalValues()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {"flags": [
                {"id": 1, "key": "local-flag", "active": true,
                 "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}},
                {"id": 2, "key": "unrequested-flag", "active": true,
                 "filters": {"groups": [{"properties": [{"key": "country", "type": "person", "value": "US", "operator": "exact"}],
                                          "rollout_percentage": 100}]}}
            ]}
            """);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"local-flag": false, "remote-flag": true, "extra-flag": true}}""");
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user-1",
            new AllFeatureFlagsOptions { FlagKeysToEvaluate = ["local-flag", "remote-flag"] },
            CancellationToken.None);

        Assert.Equal(2, snapshot.Keys.Count);
        Assert.True(snapshot.IsEnabled("local-flag"));
        Assert.True(snapshot.IsEnabled("remote-flag"));
        Assert.DoesNotContain("extra-flag", snapshot.Keys);
        Assert.DoesNotContain("unrequested-flag", snapshot.Keys);

        var request = flagsHandler.ReceivedRequests.Single();
        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var flagKeys = doc.RootElement.GetProperty("flag_keys_to_evaluate")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToArray();
        Assert.Equal(new[] { "local-flag", "remote-flag" }, flagKeys);
    }

    [Fact]
    public async Task EmptyRemoteResultForMissingKeyIsRetriedForSameInputs()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        AddResolvableLocalFlag(container);
        var firstFlagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        var secondFlagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        using var cache = new MemoryFeatureFlagCache(container.FakeTimeProvider, 100, 0.2);
        var client = container.Activate<PostHogClient>(cache);
        var options = new AllFeatureFlagsOptions { FlagKeysToEvaluate = ["local-flag", "missing-flag"] };

        var first = await client.EvaluateFlagsAsync("user-1", options, CancellationToken.None);
        var second = await client.EvaluateFlagsAsync("user-1", options, CancellationToken.None);

        Assert.True(first.IsEnabled("local-flag"));
        Assert.DoesNotContain("missing-flag", first.Keys);
        Assert.True(second.IsEnabled("local-flag"));
        Assert.DoesNotContain("missing-flag", second.Keys);
        Assert.Single(firstFlagsHandler.ReceivedRequests);
        Assert.Single(secondFlagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task ScopedFallbackBypassesDifferentlyScopedWarmCache()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {"flags": [
                {"id": 1, "key": "local-flag", "active": true,
                 "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}},
                {"id": 2, "key": "needs-server", "active": true,
                 "filters": {"groups": [{"properties": [
                     {"key": "country", "type": "person", "value": "US", "operator": "exact"}
                 ], "rollout_percentage": 100}]}}
            ]}
            """);
        var warmCacheHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"warm-only": true}}""");
        var scopedHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"remote-flag": true}}""");
        using var cache = new MemoryFeatureFlagCache(container.FakeTimeProvider, 100, 0.2);
        var client = container.Activate<PostHogClient>(cache);

        await client.GetAllFeatureFlagsAsync("user-1", options: null, CancellationToken.None);
        var snapshot = await client.EvaluateFlagsAsync(
            "user-1",
            new AllFeatureFlagsOptions { FlagKeysToEvaluate = ["local-flag", "remote-flag"] },
            CancellationToken.None);

        Assert.True(snapshot.IsEnabled("local-flag"));
        Assert.True(snapshot.IsEnabled("remote-flag"));
        Assert.DoesNotContain("warm-only", snapshot.Keys);
        Assert.Single(warmCacheHandler.ReceivedRequests);
        Assert.Single(scopedHandler.ReceivedRequests);
    }

    [Theory]
    [InlineData("{\"featureFlags\": {}, \"quotaLimited\": [\"feature_flags\"]}")]
    [InlineData("{\"featureFlags\": {}, \"errorsWhileComputingFlags\": true}")]
    public async Task InconclusiveRemoteResultForMissingKeyIsRetried(string responseBody)
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        AddResolvableLocalFlag(container);
        var firstFlagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(responseBody);
        var secondFlagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(responseBody);
        using var cache = new MemoryFeatureFlagCache(container.FakeTimeProvider, 100, 0.2);
        var client = container.Activate<PostHogClient>(cache);
        var options = new AllFeatureFlagsOptions { FlagKeysToEvaluate = ["local-flag", "missing-flag"] };

        await client.EvaluateFlagsAsync("user-1", options, CancellationToken.None);
        await client.EvaluateFlagsAsync("user-1", options, CancellationToken.None);

        Assert.Single(firstFlagsHandler.ReceivedRequests);
        Assert.Single(secondFlagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task FailedRemoteResultForMissingKeyIsRetried()
    {
        var container = new TestContainer(services => services.Configure<PostHogOptions>(options =>
        {
            options.SecretKey = "fake-personal-api-key";
            options.FeatureFlagRequestMaxRetries = 0;
        }));
        AddResolvableLocalFlag(container);
        var failedHandler = container.FakeHttpMessageHandler.AddFlagsResponseException(
            new HttpRequestException("Network error"));
        var successfulHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"missing-flag": true}}""");
        using var cache = new MemoryFeatureFlagCache(container.FakeTimeProvider, 100, 0.2);
        var client = container.Activate<PostHogClient>(cache);
        var options = new AllFeatureFlagsOptions { FlagKeysToEvaluate = ["local-flag", "missing-flag"] };

        var first = await client.EvaluateFlagsAsync("user-1", options, CancellationToken.None);
        var second = await client.EvaluateFlagsAsync("user-1", options, CancellationToken.None);

        Assert.DoesNotContain("missing-flag", first.Keys);
        Assert.True(second.IsEnabled("missing-flag"));
        Assert.Single(failedHandler.ReceivedRequests);
        Assert.Single(successfulHandler.ReceivedRequests);
    }

    [Fact]
    public async Task UnscopedSuccessfulFallbackKeepsRawRemoteResultInCache()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {"flags": [
                {"id": 1, "key": "local-flag", "active": true,
                 "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}},
                {"id": 2, "key": "unrequested-flag", "active": true,
                 "filters": {"groups": [{"properties": [{"key": "country", "type": "person", "value": "US", "operator": "exact"}],
                                          "rollout_percentage": 100}]}}
            ]}
            """);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"local-flag": false, "remote-flag": true}}""");
        using var cache = new MemoryFeatureFlagCache(container.FakeTimeProvider, 100, 0.2);
        var client = container.Activate<PostHogClient>(cache);
        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        var rawFlags = await client.GetAllFeatureFlagsAsync("user-1", options: null, CancellationToken.None);

        Assert.True(snapshot.IsEnabled("local-flag"));
        Assert.True(snapshot.IsEnabled("remote-flag"));
        Assert.False(rawFlags["local-flag"].IsEnabled);
        Assert.True(rawFlags["remote-flag"].IsEnabled);
        Assert.Single(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task RequestedLocalFlagIgnoresUnrequestedInconclusiveDefinition()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {"flags": [
                {"id": 1, "key": "local-flag", "active": true,
                 "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}},
                {"id": 2, "key": "unrequested-flag", "active": true,
                 "filters": {"groups": [{"properties": [{"key": "country", "type": "person", "value": "US", "operator": "exact"}],
                                          "rollout_percentage": 100}]}}
            ]}
            """);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"unrequested-flag": true}}""");
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user-1",
            new AllFeatureFlagsOptions { FlagKeysToEvaluate = ["local-flag"] },
            CancellationToken.None);

        Assert.True(snapshot.IsEnabled("local-flag"));
        Assert.Single(snapshot.Keys);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task MixedLocalAndRemoteEvaluationMergesRecordsAndTagsSourceCorrectly()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        // Local-only flag resolves locally; needs-remote requires a property the local pass doesn't have
        // and falls back to the remote /flags response.
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {"flags": [
                {"id": 1, "key": "local-only", "active": true, "rollout_percentage": 100,
                 "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}},
                {"id": 2, "key": "needs-remote", "active": true, "rollout_percentage": 100,
                 "filters": {"groups": [{"properties": [{"key": "country", "type": "person", "value": "US", "operator": "exact"}],
                                          "rollout_percentage": 100}]}}
            ]}
            """);
        container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"needs-remote": "variant-x", "remote-only": true}}""");
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);

        Assert.True(snapshot.IsEnabled("local-only"));
        Assert.Equal("variant-x", snapshot.GetFlag("needs-remote")?.VariantKey);
        Assert.True(snapshot.IsEnabled("remote-only"));

        await client.FlushAsync();
        using var doc = JsonDocument.Parse(batchHandler.GetReceivedRequestBody(indented: false));
        var byFlag = doc.RootElement.GetProperty("batch").EnumerateArray()
            .ToDictionary(
                e => e.GetProperty("properties").GetProperty("$feature_flag").GetString() ?? string.Empty,
                e => e.GetProperty("properties").GetProperty("locally_evaluated").GetBoolean());
        Assert.True(byFlag["local-only"]);
        Assert.False(byFlag["needs-remote"]);
        Assert.False(byFlag["remote-only"]);
    }

    [Fact]
    public async Task SendsMinimalFeatureFlagCalledEventsPerRecordWhenGatedAndFlagHasNoExperiment()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        // local-flag resolves locally; needs-remote and exp-flag fall back to the remote /flags
        // response. Both sources report the minimal gate; exp-flag is experiment-linked so it
        // still sends the full event.
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {"minimal_flag_called_events": true,
             "flags": [
                {"id": 1, "key": "local-flag", "active": true, "has_experiment": false,
                 "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}},
                {"id": 2, "key": "needs-remote", "active": true,
                 "filters": {"groups": [{"properties": [{"key": "country", "type": "person", "value": "US", "operator": "exact"}],
                                          "rollout_percentage": 100}]}}
            ]}
            """);
        container.FakeHttpMessageHandler.AddFlagsResponse(
            """
            {"minimalFlagCalledEvents": true,
             "flags": {
                "needs-remote": {
                    "key": "needs-remote",
                    "enabled": true,
                    "reason": {"description": "matched condition set 1"},
                    "metadata": {"id": 2, "version": 3, "has_experiment": false}
                },
                "exp-flag": {
                    "key": "exp-flag",
                    "enabled": true,
                    "reason": {"description": "matched condition set 1"},
                    "metadata": {"id": 3, "version": 1, "has_experiment": true}
                }
            }}
            """);
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        snapshot.IsEnabled("local-flag");
        snapshot.IsEnabled("needs-remote");
        snapshot.IsEnabled("exp-flag");

        await client.FlushAsync();
        using var doc = JsonDocument.Parse(batchHandler.GetReceivedRequestBody(indented: false));
        var byFlag = doc.RootElement.GetProperty("batch").EnumerateArray()
            .ToDictionary(
                e => e.GetProperty("properties").GetProperty("$feature_flag").GetString() ?? string.Empty,
                e => e.GetProperty("properties"));

        // Minimal events strip the $feature/<key> enumeration and (for local evaluation) the
        // definitions-loaded timestamp.
        Assert.False(byFlag["local-flag"].TryGetProperty("$feature/local-flag", out _));
        Assert.False(byFlag["local-flag"].TryGetProperty("$feature_flag_definitions_loaded_at", out _));
        Assert.False(byFlag["local-flag"].GetProperty("$feature_flag_has_experiment").GetBoolean());
        Assert.False(byFlag["needs-remote"].TryGetProperty("$feature/needs-remote", out _));
        Assert.False(byFlag["needs-remote"].GetProperty("$feature_flag_has_experiment").GetBoolean());

        // Experiment-linked flags keep the full event.
        Assert.True(byFlag["exp-flag"].TryGetProperty("$feature/exp-flag", out _));
        Assert.True(byFlag["exp-flag"].GetProperty("$feature_flag_has_experiment").GetBoolean());
    }

    [Fact]
    public async Task SendsFullFeatureFlagCalledEventPerRecordWhenHasExperimentIsUnknownEvenWhenGated()
    {
        var container = new TestContainer();
        // Gate is on, but the flag's metadata omits has_experiment entirely: an unknown signal
        // must still fall back to the full event on the snapshot path.
        container.FakeHttpMessageHandler.AddFlagsResponse(
            """
            {"minimalFlagCalledEvents": true,
             "flags": {
                "flag-key": {
                    "key": "flag-key",
                    "enabled": true,
                    "reason": {"description": "matched condition set 1"},
                    "metadata": {"id": 1, "version": 1}
                }
            }}
            """);
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        snapshot.IsEnabled("flag-key");

        await client.FlushAsync();
        using var doc = JsonDocument.Parse(batchHandler.GetReceivedRequestBody(indented: false));
        var properties = doc.RootElement.GetProperty("batch").EnumerateArray().Single().GetProperty("properties");

        Assert.True(properties.TryGetProperty("$feature/flag-key", out _));
        Assert.False(properties.TryGetProperty("$feature_flag_has_experiment", out _));
    }

    [Fact]
    public async Task UnknownKeyAccessAppendsFlagMissingErrorOnFeatureFlagCalled()
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"known": true}}""");
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        snapshot.IsEnabled("does-not-exist");

        await client.FlushAsync();
        using var doc = JsonDocument.Parse(batchHandler.GetReceivedRequestBody(indented: false));
        var props = doc.RootElement.GetProperty("batch").EnumerateArray().Single().GetProperty("properties");
        Assert.Equal("flag_missing", props.GetProperty("$feature_flag_error").GetString());
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoteMalformedPayloadIsAbsentAndKeepsSiblingFlagsAndPayloads(string serializedPayload)
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddFlagsResponse(
            """
            {
                "featureFlags": {"checkout": "blue", "beta-ui": true},
                "featureFlagPayloads": {"checkout": PAYLOAD, "beta-ui": "{\"color\":\"green\"}"}
            }
            """.Replace("PAYLOAD", JsonSerializer.Serialize(serializedPayload), StringComparison.Ordinal));
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);

        Assert.Equal("blue", snapshot.GetFlag("checkout")?.VariantKey);
        Assert.Null(snapshot.GetFlagPayload("checkout"));
        Assert.True(snapshot.IsEnabled("beta-ui"));
        Assert.Equal(
            """{"color":"green"}""",
            snapshot.GetFlagPayload("beta-ui")?.RootElement.GetRawText());
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LocallyEvaluatedMalformedPayloadIsAbsentWithoutFallingBackToRemote(string serializedPayload)
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {"flags": [
                {"id": 1, "key": "checkout", "active": true,
                 "filters": {"groups": [{"properties": [], "rollout_percentage": 100}],
                             "payloads": {"true": PAYLOAD}}}
            ]}
            """.Replace("PAYLOAD", JsonSerializer.Serialize(serializedPayload), StringComparison.Ordinal));
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);

        Assert.True(snapshot.IsEnabled("checkout"));
        Assert.Null(snapshot.GetFlagPayload("checkout"));
        Assert.Empty(flagsHandler.ReceivedRequests);
    }
}

public class TheSnapshotAccessMethods
{
    [Theory]
    [InlineData("IsEnabled")]
    [InlineData("GetFlag")]
    public async Task UnknownKeyReturnsFalsyValue(string accessor)
    {
        var snapshot = await EvaluateAsync("""{"featureFlags": {"known": true}}""");
        switch (accessor)
        {
            case "IsEnabled":
                Assert.False(snapshot.IsEnabled("unknown"));
                break;
            case "GetFlag":
                Assert.Null(snapshot.GetFlag("unknown"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(accessor), accessor, "Unknown accessor");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownKeyReturnsCallerSuppliedDefaultValue(bool defaultValue)
    {
        var snapshot = await EvaluateAsync("""{"featureFlags": {}}""");

        Assert.Equal(defaultValue, snapshot.IsEnabled("missing", defaultValue));
    }

    [Theory]
    [InlineData("false", true, false)]
    [InlineData("\"variant-a\"", false, true)]
    public async Task PresentFlagValueWinsOverCallerSuppliedDefaultValue(
        string flagValue,
        bool defaultValue,
        bool expected)
    {
        var snapshot = await EvaluateAsync($$$"""{"featureFlags": {"feature": {{{flagValue}}} }}""");

        Assert.Equal(expected, snapshot.IsEnabled("feature", defaultValue));
    }

    [Fact]
    public async Task GetFlagPayloadDoesNotFireFeatureFlagCalledEvent()
    {
        var (snapshot, batchHandler, client) = await EvaluateWithBatchAsync(
            """{"featureFlags": {"flag-a": true}, "featureFlagPayloads": {"flag-a": "\"hello\""}}""");

        var payload = snapshot.GetFlagPayload("flag-a");
        Assert.NotNull(payload);
        Assert.Equal("hello", payload.RootElement.GetString());

        await client.FlushAsync();
        Assert.Empty(batchHandler.ReceivedRequests);
    }

    [Fact]
    public async Task IsEnabledFiresFeatureFlagCalledEventOncePerDistinctIdKeyResponse()
    {
        var (snapshot, batchHandler, client) = await EvaluateWithBatchAsync(
            """{"featureFlags": {"flag-a": true}}""");

        Assert.True(snapshot.IsEnabled("flag-a"));
        Assert.True(snapshot.IsEnabled("flag-a")); // dedup
        Assert.True(snapshot.IsEnabled("flag-a")); // dedup

        await client.FlushAsync();
        var body = batchHandler.GetReceivedRequestBody(indented: false);
        var matches = System.Text.RegularExpressions.Regex.Matches(body, "\\$feature_flag_called");
        Assert.Single(matches);
    }

    [Fact]
    public async Task EmptyDistinctIdSnapshotDoesNotFireEvents()
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"flag-a": true}}""");
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(string.Empty, options: null, CancellationToken.None);
        snapshot.IsEnabled("anything");
        snapshot.GetFlag("anything");

        await client.FlushAsync();
        Assert.Empty(batchHandler.ReceivedRequests);
    }

    [Fact]
    public async Task LocallyEvaluatedFlagSnapshotTagsLocallyEvaluatedAndReasonAndDefinitionsLoadedAt()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {"flags": [{"id": 1, "key": "flag-a", "active": true, "rollout_percentage": 100, "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}}]}
            """);
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user-1",
            new AllFeatureFlagsOptions { OnlyEvaluateLocally = true },
            CancellationToken.None);
        Assert.True(snapshot.IsEnabled("flag-a"));

        await client.FlushAsync();
        var body = batchHandler.GetReceivedRequestBody(indented: false);
        Assert.Contains("\"locally_evaluated\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"$feature_flag_reason\":\"Evaluated locally\"", body, StringComparison.Ordinal);
        Assert.Contains("\"$feature_flag_definitions_loaded_at\":1705864103000", body, StringComparison.Ordinal);
    }

    static async Task<FeatureFlagEvaluations> EvaluateAsync(string flagsResponseBody)
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddFlagsResponse(flagsResponseBody);
        var client = container.Activate<PostHogClient>();
        return await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
    }

    static async Task<(FeatureFlagEvaluations snapshot, FakeHttpMessageHandler.RequestHandler batchHandler, PostHogClient client)>
        EvaluateWithBatchAsync(string flagsResponseBody)
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddFlagsResponse(flagsResponseBody);
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        return (snapshot, batchHandler, client);
    }
}

public class TheSnapshotFilterMethods
{
    [Fact]
    public async Task OnlyAccessedReturnsAccessedFlagsOnly()
    {
        var snapshot = await EvaluateAsync("""{"featureFlags": {"a": true, "b": true, "c": true}}""");

        snapshot.IsEnabled("a");
        snapshot.GetFlag("c");

        var accessed = snapshot.OnlyAccessed();
        Assert.Equal(2, accessed.Keys.Count);
        Assert.Contains("a", accessed.Keys);
        Assert.Contains("c", accessed.Keys);
    }

    [Fact]
    public async Task OnlyAccessedFallsBackToAllFlagsAndWarnsWhenNothingAccessed()
    {
        var (snapshot, container) = await EvaluateAsyncWithContainer("""{"featureFlags": {"a": true, "b": true}}""");

        var fallback = snapshot.OnlyAccessed();

        Assert.Equal(2, fallback.Keys.Count);
        Assert.Contains(
            container.FakeLoggerProvider.GetAllEvents(),
            e => e.LogLevel == LogLevel.Warning
                && (e.Message ?? string.Empty).Contains("OnlyAccessed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnlyAccessedDoesNotWarnWhenLogWarningsDisabled()
    {
        var container = new TestContainer(services =>
        {
            services.Configure<PostHogOptions>(options =>
            {
                options.ProjectToken = "fake-project-token";
                options.FeatureFlagsLogWarnings = false;
            });
        });
        container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"a": true}}""");
        var client = container.Activate<PostHogClient>();
        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);

        snapshot.OnlyAccessed();

        Assert.DoesNotContain(
            container.FakeLoggerProvider.GetAllEvents(),
            e => e.LogLevel == LogLevel.Warning
                && (e.Message ?? string.Empty).Contains("OnlyAccessed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnlyDropsUnknownKeysWithWarning()
    {
        var (snapshot, container) = await EvaluateAsyncWithContainer("""{"featureFlags": {"a": true, "b": true}}""");

        var only = snapshot.Only("a", "missing-1", "missing-2");

        Assert.Single(only.Keys);
        Assert.Contains("a", only.Keys);
        Assert.Contains(
            container.FakeLoggerProvider.GetAllEvents(),
            e => e.LogLevel == LogLevel.Warning
                && (e.Message ?? string.Empty).Contains("missing-1", StringComparison.Ordinal)
                && (e.Message ?? string.Empty).Contains("missing-2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FilteredSnapshotDoesNotBackPropagateAccessToParent()
    {
        var snapshot = await EvaluateAsync("""{"featureFlags": {"a": true, "b": true}}""");
        snapshot.IsEnabled("a"); // parent has accessed "a"

        var child = snapshot.OnlyAccessed();
        child.IsEnabled("b" /* will be missing in child but still records access on the child */);

        var parentAccessed = snapshot.OnlyAccessed();
        // Parent should still only have "a" accessed; the child's access of "b" should not leak.
        Assert.Single(parentAccessed.Keys);
        Assert.Contains("a", parentAccessed.Keys);
    }

    static async Task<FeatureFlagEvaluations> EvaluateAsync(string flagsResponseBody)
    {
        var (snapshot, _) = await EvaluateAsyncWithContainer(flagsResponseBody);
        return snapshot;
    }

    static async Task<(FeatureFlagEvaluations snapshot, TestContainer container)> EvaluateAsyncWithContainer(string flagsResponseBody)
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddFlagsResponse(flagsResponseBody);
        container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        return (snapshot, container);
    }
}

public class TheCaptureWithFlagsSnapshotMethod
{
    [Fact]
    public async Task LegacyAndSnapshotInputsProduceIdenticalFeatureFlagProperties()
    {
        const string response =
            """{"featureFlags": {"flag-a": true, "flag-b": false, "flag-c": "variant-x"}}""";
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddRepeatedFlagsResponse(2, response);
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        client.Capture("user-1", "snapshot", properties: null, groups: null, flags: snapshot);
#pragma warning disable CS0618
        client.Capture("user-1", "legacy", properties: null, groups: null, sendFeatureFlags: true);
#pragma warning restore CS0618
        await client.FlushAsync();

        using var doc = JsonDocument.Parse(batchHandler.GetReceivedRequestBody(indented: false));
        var events = doc.RootElement.GetProperty("batch").EnumerateArray().ToArray();
        var snapshotProperties = events.Single(e => e.GetProperty("event").GetString() == "snapshot")
            .GetProperty("properties");
        var legacyProperties = events.Single(e => e.GetProperty("event").GetString() == "legacy")
            .GetProperty("properties");

        foreach (var propertyName in new[]
                 {
                     "$feature/flag-a",
                     "$feature/flag-b",
                     "$feature/flag-c",
                     "$active_feature_flags"
                 })
        {
            Assert.Equal(
                legacyProperties.GetProperty(propertyName).GetRawText(),
                snapshotProperties.GetProperty(propertyName).GetRawText());
        }
    }

    [Fact]
    public async Task AttachesFeatureFlagPropertiesAndActiveFeatureFlagsFromSnapshot()
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"flag-a": true, "flag-b": false, "flag-c": "variant-x"}}""");
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        client.Capture("user-1", "page_viewed", properties: null, groups: null, flags: snapshot);
        await client.FlushAsync();

        var body = batchHandler.GetReceivedRequestBody(indented: false);
        Assert.Contains("\"$feature/flag-a\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"$feature/flag-b\":false", body, StringComparison.Ordinal);
        Assert.Contains("\"$feature/flag-c\":\"variant-x\"", body, StringComparison.Ordinal);

        // Parse and assert order-independently — Dictionary iteration order is preserved in
        // practice but is not a language guarantee.
        using var doc = JsonDocument.Parse(body);
        var properties = doc.RootElement
            .GetProperty("batch")
            .EnumerateArray()
            .Single()
            .GetProperty("properties");
        var activeFeatureFlags = properties.GetProperty("$active_feature_flags")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "flag-a", "flag-c" }, activeFeatureFlags);
    }

    [Fact]
    public async Task DoesNotMakeAdditionalFlagsHttpRequest()
    {
        var container = new TestContainer();
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"flag-a": true}}""");
        var extraRequest = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        client.Capture("user-1", "page_viewed", properties: null, groups: null, flags: snapshot);
        await client.FlushAsync();

        Assert.Single(flagsHandler.ReceivedRequests);
        Assert.Empty(extraRequest.ReceivedRequests);
        using var document = JsonDocument.Parse(batchHandler.GetReceivedRequestBody(indented: false));
        var captured = Assert.Single(document.RootElement.GetProperty("batch").EnumerateArray());
        Assert.Equal("page_viewed", captured.GetProperty("event").GetString());
        Assert.True(captured.GetProperty("properties").GetProperty("$feature/flag-a").GetBoolean());
    }

    [Fact]
    public async Task SharesDedupCacheWithLegacySingleFlagPath()
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddRepeatedFlagsResponse(2, """{"featureFlags": {"flag-a": true}}""");
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        // Legacy path fires $feature_flag_called for ("user-1", "flag-a", true).
        // The legacy single-flag method is deprecated but still must dedup against the snapshot path.
#pragma warning disable CS0618
        Assert.True(await client.IsFeatureEnabledAsync("flag-a", "user-1"));
#pragma warning restore CS0618

        // Snapshot path accesses the same flag — should hit the existing cache and NOT fire again.
        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        snapshot.IsEnabled("flag-a");

        await client.FlushAsync();
        var body = batchHandler.GetReceivedRequestBody(indented: false);
        var matches = System.Text.RegularExpressions.Regex.Matches(body, "\\$feature_flag_called");
        Assert.Single(matches);
    }

    [Fact]
    public async Task CaptureExceptionAttachesFeatureFlagsFromSnapshot()
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"flag-a": true, "flag-b": "variant-x"}}""");
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        client.CaptureException(
            new InvalidOperationException("boom"),
            "user-1",
            properties: null,
            groups: null,
            flags: snapshot);

        await client.FlushAsync();
        using var doc = JsonDocument.Parse(batchHandler.GetReceivedRequestBody(indented: false));
        var exceptionEvent = doc.RootElement.GetProperty("batch")
            .EnumerateArray()
            .Single(e => e.GetProperty("event").GetString() == "$exception");
        var props = exceptionEvent.GetProperty("properties");
        Assert.True(props.GetProperty("$feature/flag-a").GetBoolean());
        Assert.Equal("variant-x", props.GetProperty("$feature/flag-b").GetString());
    }
}

public class TheUnresolvedFlagsProperty
{
    const string BetaUiResolvesTrue = """
        {"id": 1, "key": "beta-ui", "active": true,
         "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}}
        """;

    const string CheckoutWithExperienceContinuity = """
        {"id": 2, "key": "checkout", "active": true, "ensure_experience_continuity": true,
         "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}}
        """;

    const string BetaUiDependsOnCheckout = """
        {"id": 1, "key": "beta-ui", "active": true,
         "filters": {"groups": [{"properties": [
             {"key": "checkout", "type": "flag", "value": true, "operator": "flag_evaluates_to",
              "dependency_chain": ["checkout"]}],
          "rollout_percentage": 100}]}}
        """;

    const string PlanIsPro = """{"key": "plan", "type": "person", "value": "pro", "operator": "exact"}""";

    const string StaticCohort = """{"key": "id", "type": "cohort", "value": 999}""";

    static readonly AllFeatureFlagsOptions LocalOnly = new() { OnlyEvaluateLocally = true };

    static string CheckoutWithFilters(params string[] filters) => $$$"""
        {"id": 2, "key": "checkout", "active": true,
         "filters": {"groups": [{"properties": [{{{string.Join(", ", filters)}}}], "rollout_percentage": 100}]}}
        """;

    static TestContainer CreateContainer(params string[] flags)
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        // Served verbatim: the typed overload cannot serialize a boolean flag dependency value.
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            $$$"""{"flags": [{{{string.Join(", ", flags)}}}], "cohorts": {}}""",
            "\"definitions\"");
        return container;
    }

    static async Task<JsonElement[]> GetFeatureFlagCalledEventsAsync(
        PostHogClient client,
        FakeHttpMessageHandler.RequestHandler batchHandler)
    {
        await client.FlushAsync();
        if (batchHandler.ReceivedRequests.Count == 0)
        {
            return [];
        }
        using var doc = JsonDocument.Parse(batchHandler.GetReceivedRequestBody(indented: false));
        return doc.RootElement.GetProperty("batch").EnumerateArray()
            .Where(e => e.GetProperty("event").GetString() == "$feature_flag_called")
            .Select(e => e.GetProperty("properties").Clone())
            .ToArray();
    }

    static async Task<string?> GetFeatureFlagErrorAsync(
        PostHogClient client,
        FakeHttpMessageHandler.RequestHandler batchHandler,
        string key)
    {
        var properties = (await GetFeatureFlagCalledEventsAsync(client, batchHandler))
            .Single(p => p.GetProperty("$feature_flag").GetString() == key);
        return properties.TryGetProperty("$feature_flag_error", out var error) ? error.GetString() : null;
    }

    [Fact]
    public async Task LocalOnlyEvaluationReportsExperienceContinuityFlagAsUnresolved()
    {
        var container = CreateContainer(BetaUiResolvesTrue, CheckoutWithExperienceContinuity);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-123", LocalOnly, CancellationToken.None);

        Assert.True(snapshot.Records["beta-ui"].Enabled);
        Assert.DoesNotContain("checkout", snapshot.Keys);
        var unresolved = Assert.Single(snapshot.UnresolvedFlags);
        Assert.Equal("checkout", unresolved.Key);
        Assert.Equal(UnresolvedFlagReason.ExperienceContinuity, unresolved.Value);
        Assert.Equal("experience_continuity", unresolved.Value.ToValueString());
        Assert.Empty(flagsHandler.ReceivedRequests);
        Assert.Empty(await GetFeatureFlagCalledEventsAsync(client, batchHandler));
        Assert.Empty(snapshot.OnlyAccessed().UnresolvedFlags);
        Assert.Empty(snapshot.Only("beta-ui", "checkout").UnresolvedFlags);
    }

    public static IEnumerable<object[]> InconclusiveCauses()
    {
        yield return [CheckoutWithFilters(PlanIsPro), null!, UnresolvedFlagReason.MissingContext];
        yield return [
            CheckoutWithFilters("""{"key": "plan", "type": "person", "value": "pro", "operator": "operator_from_the_future"}"""),
            "enterprise",
            UnresolvedFlagReason.UnsupportedDefinition];
        yield return [CheckoutWithFilters(StaticCohort), null!, UnresolvedFlagReason.UnsupportedDefinition];
        yield return [CheckoutWithFilters(StaticCohort, PlanIsPro), null!, UnresolvedFlagReason.UnsupportedDefinition];
        yield return [CheckoutWithFilters(PlanIsPro, StaticCohort), null!, UnresolvedFlagReason.UnsupportedDefinition];
    }

    [Theory]
    [MemberData(nameof(InconclusiveCauses))]
    public async Task EachInconclusiveCauseReportsItsUnresolvedReason(
        string checkoutDefinition,
        string? plan,
        UnresolvedFlagReason expectedReason)
    {
        var container = CreateContainer(checkoutDefinition);
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user-123",
            new AllFeatureFlagsOptions
            {
                OnlyEvaluateLocally = true,
                PersonProperties = plan is null ? [] : new() { ["plan"] = plan }
            },
            CancellationToken.None);

        Assert.DoesNotContain("checkout", snapshot.Keys);
        Assert.Equal(expectedReason, snapshot.UnresolvedFlags["checkout"]);
    }

    [Fact]
    public async Task FlagBlockedByUnresolvedDependencyReportsTheDependency()
    {
        var container = CreateContainer(CheckoutWithExperienceContinuity, BetaUiDependsOnCheckout);
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user-123",
            new AllFeatureFlagsOptions { OnlyEvaluateLocally = true, FlagKeysToEvaluate = ["beta-ui"] },
            CancellationToken.None);

        Assert.DoesNotContain("beta-ui", snapshot.Keys);
        Assert.Equal(UnresolvedFlagReason.UnresolvedDependency, snapshot.UnresolvedFlags["beta-ui"]);
        Assert.False(snapshot.UnresolvedFlags.ContainsKey("checkout"));
    }

    const string TargetDependsOnDep = """
        {"id": 3, "key": "target", "active": true,
         "filters": {
             "multivariate": {"variants": [{"key": "a", "rollout_percentage": 50}, {"key": "b", "rollout_percentage": 50}]},
             "groups": [
                 {"properties": [{"key": "dep", "type": "flag", "value": true, "operator": "flag_evaluates_to",
                                  "dependency_chain": ["dep"]}],
                  "rollout_percentage": 100, "variant": "a"},
                 {"properties": [], "rollout_percentage": 100, "variant": "b"}]}}
        """;

    static string DepWithFilter(string filter) => $$$"""
        {"id": 4, "key": "dep", "active": true,
         "filters": {"groups": [{"properties": [{{{filter}}}], "rollout_percentage": 100}]}}
        """;

    static async Task<string?> ReadTargetVariantAsync(PostHogClient client, string read)
    {
        var personProperties = new Dictionary<string, object?> { ["plan"] = "pro" };
        if (read == "get-feature-flag")
        {
#pragma warning disable CS0618
            var flag = await client.GetFeatureFlagAsync(
                "target",
                "user-123",
                new FeatureFlagOptions { PersonProperties = personProperties },
                CancellationToken.None);
#pragma warning restore CS0618
            return flag?.VariantKey;
        }

        var snapshot = await client.EvaluateFlagsAsync(
            "user-123",
            new AllFeatureFlagsOptions
            {
                PersonProperties = personProperties,
                FlagKeysToEvaluate = read == "scoped-snapshot" ? ["target"] : null
            },
            CancellationToken.None);
        return snapshot.GetFlag("target")?.VariantKey;
    }

    [Theory]
    [InlineData("get-feature-flag")]
    [InlineData("scoped-snapshot")]
    [InlineData("all-flags-snapshot")]
    public async Task FlagWhoseDependencyNeedsServerDataFallsBackToTheServer(string read)
    {
        var container = CreateContainer(DepWithFilter(StaticCohort), TargetDependsOnDep);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"dep": true, "target": "a"}}""");
        var client = container.Activate<PostHogClient>();

        Assert.Equal("a", await ReadTargetVariantAsync(client, read));
        Assert.Single(flagsHandler.ReceivedRequests);
    }

    [Theory]
    [InlineData("get-feature-flag")]
    [InlineData("scoped-snapshot")]
    [InlineData("all-flags-snapshot")]
    public async Task FlagWhoseDependencyResolvesLocallyDoesNotCallTheServer(string read)
    {
        var container = CreateContainer(DepWithFilter(PlanIsPro), TargetDependsOnDep);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"dep": true, "target": "b"}}""");
        var client = container.Activate<PostHogClient>();

        Assert.Equal("a", await ReadTargetVariantAsync(client, read));
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task LocalOnlyEvaluationReportsFlagWhoseDependencyNeedsServerDataAsUnresolvedDependency()
    {
        var container = CreateContainer(DepWithFilter(StaticCohort), TargetDependsOnDep);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"dep": true, "target": "a"}}""");
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-123", LocalOnly, CancellationToken.None);

        Assert.DoesNotContain("target", snapshot.Keys);
        Assert.Equal(UnresolvedFlagReason.UnresolvedDependency, snapshot.UnresolvedFlags["target"]);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task FlagThatDependsOnFlagWithNoLoadedDefinitionReportsUnsupportedDefinition()
    {
        var container = CreateContainer(BetaUiDependsOnCheckout);
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user-123",
            new AllFeatureFlagsOptions { OnlyEvaluateLocally = true, FlagKeysToEvaluate = ["beta-ui"] },
            CancellationToken.None);

        Assert.DoesNotContain("beta-ui", snapshot.Keys);
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, snapshot.UnresolvedFlags["beta-ui"]);
        Assert.False(snapshot.UnresolvedFlags.ContainsKey("checkout"));
    }

    [Fact]
    public async Task FlagResolvedByRemoteFallbackIsNotUnresolvedAndReportsNoError()
    {
        var container = CreateContainer(CheckoutWithExperienceContinuity);
        container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"checkout": true}}""");
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-123", options: null, CancellationToken.None);

        Assert.Empty(snapshot.UnresolvedFlags);
        Assert.True(snapshot.IsEnabled("checkout"));
        var properties = Assert.Single(await GetFeatureFlagCalledEventsAsync(client, batchHandler));
        Assert.Equal("checkout", properties.GetProperty("$feature_flag").GetString());
        Assert.True(properties.GetProperty("$feature_flag_response").GetBoolean());
        Assert.False(properties.TryGetProperty("$feature_flag_error", out _));
    }

    [Theory]
    [InlineData(null, "local_evaluation_inconclusive")]
    [InlineData("""{"featureFlags": {}}""", "local_evaluation_inconclusive")]
    [InlineData("""{"featureFlags": {}, "errorsWhileComputingFlags": true}""", "errors_while_computing_flags,local_evaluation_inconclusive")]
    public async Task RemoteFallbackThatDoesNotResolveTheFlagLeavesItUnresolved(string? flagsResponse, string expectedError)
    {
        var container = new TestContainer(services => services.Configure<PostHogOptions>(options =>
        {
            options.SecretKey = "fake-personal-api-key";
            options.FeatureFlagRequestMaxRetries = 0;
        }));
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            $$"""{"flags": [{{CheckoutWithExperienceContinuity}}]}""");
        var flagsHandler = flagsResponse is null
            ? container.FakeHttpMessageHandler.AddFlagsResponseException(new HttpRequestException("Network error"))
            : container.FakeHttpMessageHandler.AddFlagsResponse(flagsResponse);
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-123", options: null, CancellationToken.None);

        Assert.Single(flagsHandler.ReceivedRequests);
        Assert.DoesNotContain("checkout", snapshot.Keys);
        Assert.Equal(UnresolvedFlagReason.ExperienceContinuity, snapshot.UnresolvedFlags["checkout"]);
        Assert.False(snapshot.IsEnabled("checkout"));
        Assert.Equal(
            flagsResponse is null ? "unknown_error,local_evaluation_inconclusive" : expectedError,
            await GetFeatureFlagErrorAsync(client, batchHandler, "checkout"));
    }

    [Fact]
    public async Task ReadingUnresolvedFlagReportsInconclusiveLocalEvaluation()
    {
        var container = CreateContainer(CheckoutWithExperienceContinuity);
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-123", LocalOnly, CancellationToken.None);

        Assert.False(snapshot.IsEnabled("checkout"));
        Assert.Equal("local_evaluation_inconclusive", await GetFeatureFlagErrorAsync(client, batchHandler, "checkout"));
    }

    [Fact]
    public async Task FilteredSnapshotReportsUnresolvedKeyAsMissing()
    {
        var container = CreateContainer(BetaUiResolvesTrue, CheckoutWithExperienceContinuity);
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-123", LocalOnly, CancellationToken.None);
        var filtered = snapshot.Only("beta-ui", "checkout");

        Assert.Empty(filtered.UnresolvedFlags);
        Assert.False(filtered.IsEnabled("checkout"));
        Assert.Equal("flag_missing", await GetFeatureFlagErrorAsync(client, batchHandler, "checkout"));
    }

    [Fact]
    public async Task KeyWithoutLocalDefinitionIsMissingNotUnresolved()
    {
        var container = CreateContainer(BetaUiResolvesTrue);
        container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user-123",
            new AllFeatureFlagsOptions { FlagKeysToEvaluate = ["beta-ui", "typo-flag"] },
            CancellationToken.None);
        snapshot.IsEnabled("typo-flag");

        Assert.Empty(snapshot.UnresolvedFlags);
        Assert.Equal("flag_missing", await GetFeatureFlagErrorAsync(client, batchHandler, "typo-flag"));
    }

    [Fact]
    public async Task FlagOutsideTheRequestedKeysIsNotUnresolved()
    {
        var container = CreateContainer(BetaUiResolvesTrue, CheckoutWithExperienceContinuity);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user-123",
            new AllFeatureFlagsOptions { FlagKeysToEvaluate = ["beta-ui"] },
            CancellationToken.None);

        Assert.True(snapshot.Records["beta-ui"].Enabled);
        Assert.Empty(snapshot.UnresolvedFlags);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task InactiveFlagIsResolvedAsFalseNotUnresolved()
    {
        var container = CreateContainer("""
            {"id": 2, "key": "checkout", "active": false, "ensure_experience_continuity": true,
             "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}}
            """);
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-123", LocalOnly, CancellationToken.None);

        Assert.False(snapshot.Records["checkout"].Enabled);
        Assert.Empty(snapshot.UnresolvedFlags);
    }

    [Fact]
    public async Task UnexpectedEvaluationErrorIsMissingNotUnresolved()
    {
        var container = CreateContainer(CheckoutWithFilters(PlanIsPro));
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user-123",
            new AllFeatureFlagsOptions
            {
                OnlyEvaluateLocally = true,
                PersonProperties = new() { ["plan"] = new ThrowingValue() }
            },
            CancellationToken.None);
        snapshot.IsEnabled("checkout");

        Assert.Empty(snapshot.UnresolvedFlags);
        Assert.Equal("flag_missing", await GetFeatureFlagErrorAsync(client, batchHandler, "checkout"));
    }

    [Fact]
    public async Task EmptySnapshotHasNoUnresolvedFlags()
    {
        var container = CreateContainer(CheckoutWithExperienceContinuity);
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(string.Empty, LocalOnly, CancellationToken.None);

        Assert.Empty(snapshot.UnresolvedFlags);
    }

    [Theory]
    [InlineData(UnresolvedFlagReason.ExperienceContinuity, "experience_continuity")]
    [InlineData(UnresolvedFlagReason.UnsupportedDefinition, "unsupported_definition")]
    [InlineData(UnresolvedFlagReason.UnresolvedDependency, "unresolved_dependency")]
    [InlineData(UnresolvedFlagReason.MissingContext, "missing_context")]
    public void ReasonHasTheStableSpecValue(UnresolvedFlagReason reason, string expected)
    {
        Assert.Equal(expected, reason.ToValueString());
        Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(reason));
    }

    sealed class ThrowingValue
    {
        public override string ToString() => throw new InvalidOperationException("Cannot stringify");
    }
}
