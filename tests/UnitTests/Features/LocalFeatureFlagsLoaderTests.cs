using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PostHog;
using PostHog.Api;
using PostHog.Features;
using PostHog.Json;
using UnitTests.Fakes;
using static LocalEvaluatorTests.VersionedPropertyMatchingTests;
#if NETCOREAPP3_1
using TestLibrary.Fakes.Polyfills;
#endif

namespace LocalFeatureFlagsLoaderTests;

public class VersionedDefinitionSnapshots
{
    [Fact]
    public async Task PreservesCachedSnapshotOn304AndFailureAndResetsOnVersionOnlyRefresh()
    {
        var container = new TestContainer("fake-personal-api-key");
        using var httpClient = new HttpClient(container.FakeHttpMessageHandler);
        using var apiClient = container.Activate<PostHogApiClient>(httpClient);
        await using var loader = container.Activate<LocalFeatureFlagsLoader>(apiClient);
        var handler = container.FakeHttpMessageHandler;
        handler.AddLocalEvaluationResponseWithETag(DefinitionsJson("false", "exact", 1), "\"legacy\"");
        var legacy = await loader.GetFeatureFlagsForLocalEvaluationAsync(CancellationToken.None);
        Assert.NotNull(legacy);
        Assert.Equal(true, Evaluate(legacy));

        handler.AddLocalEvaluationResponseWithETag(DefinitionsJson("false", "exact", 2), "\"explicit\"");
        var explicitEvaluator = await loader.RefreshAsync(CancellationToken.None);
        Assert.NotNull(explicitEvaluator);
        Assert.NotSame(legacy, explicitEvaluator);
        Assert.Equal(2, explicitEvaluator.LocalEvaluationApiResult.PropertyMatchingVersion);
        Assert.Equal(false, Evaluate(explicitEvaluator));
        Assert.Same(explicitEvaluator, await loader.GetFeatureFlagsForLocalEvaluationAsync(CancellationToken.None));
        Assert.Equal(true, Evaluate(legacy)); // An in-flight reader keeps its original semantics.

        var notModified = handler.AddLocalEvaluationNotModifiedResponse();
        Assert.Same(explicitEvaluator, await loader.RefreshAsync(CancellationToken.None));
        Assert.Equal("\"explicit\"", notModified.ReceivedRequest!.Headers.IfNoneMatch.Single().Tag);
        Assert.Equal(false, Evaluate(explicitEvaluator));

        handler.AddResponse(FakeHttpMessageHandlerExtensions.LocalEvaluationUrl, HttpMethod.Get, new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"type\":\"server_error\",\"detail\":\"unavailable\"}", System.Text.Encoding.UTF8, "application/json")
        });
        Assert.Same(explicitEvaluator, await loader.RefreshAsync(CancellationToken.None));
        Assert.Equal(false, Evaluate(explicitEvaluator));

        foreach (var version in new int?[] { 1, 2, null })
        {
            handler.AddLocalEvaluationResponseWithETag(DefinitionsJson("false", "exact", version), "\"refresh\"");
            var refreshed = await loader.RefreshAsync(CancellationToken.None);
            Assert.NotNull(refreshed);
            Assert.Equal(version, refreshed.LocalEvaluationApiResult.PropertyMatchingVersion);
            Assert.Equal(version != 2, Evaluate(refreshed));
        }
        Assert.Equal(false, Evaluate(explicitEvaluator));
    }

    [Fact]
    public async Task PublicSingleBulkAndFullResultsFollowVersionOnlyReloadWithoutRemoteFallback()
    {
        var container = new TestContainer("fake-personal-api-key");
        await using var client = container.Activate<PostHogClient>();
        var remote = container.FakeHttpMessageHandler.AddFlagsResponse("""{"flags": {}}""");
        var properties = new Dictionary<string, object?> { ["value"] = "banana" };
        var options = new FeatureFlagOptions { PersonProperties = properties, OnlyEvaluateLocally = true };
        var allOptions = new AllFeatureFlagsOptions { PersonProperties = properties, OnlyEvaluateLocally = true };
        foreach (var version in new int?[] { 1, 2, 1, 2, null })
        {
            container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(DefinitionsJson("false", "exact", version), "\"reload\"");
            await client.LoadFeatureFlagsAsync(CancellationToken.None);
#pragma warning disable CS0618 // Verify compatibility of the legacy single-flag API too.
            var single = await client.GetFeatureFlagAsync("test", "person", options, CancellationToken.None);
#pragma warning restore CS0618
            Assert.NotNull(single);
            Assert.Equal(version != 2, single.IsEnabled);
            var bulk = await client.GetAllFeatureFlagsAsync("person", allOptions, CancellationToken.None);
            Assert.Equal(version != 2, bulk["test"].IsEnabled);
            var full = await client.EvaluateFlagsAsync("person", options, CancellationToken.None);
            Assert.Contains("test", full.Keys);
            Assert.Equal(version != 2, full.IsEnabled("test"));
        }
        Assert.Empty(remote.ReceivedRequests);
    }

    static StringOrValue<bool> Evaluate(LocalEvaluator evaluator) => evaluator.EvaluateFeatureFlag(
        "test", "person", personProperties: new() { ["value"] = "banana" });
}

public class TheDisposeAsyncMethod
{
    const string LocalEvaluationResponse = """
        {
            "flags": [
                {
                    "key": "test-flag",
                    "active": true,
                    "rollout_percentage": 100,
                    "filters": {
                        "groups": [
                            {
                                "properties": [],
                                "rollout_percentage": 100
                            }
                        ]
                    }
                }
            ]
        }
        """;

    static readonly Uri LocalEvaluationUrl = FakeHttpMessageHandlerExtensions.LocalEvaluationUrl;

    [Fact]
    public async Task CompletesGracefullyDuringInFlightPoll()
    {
        var container = new TestContainer("fake-personal-api-key");
        var pollStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pollCanProceed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // First response succeeds immediately (the initial load).
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(LocalEvaluationResponse);

        // Second response (the timer-triggered poll) blocks until we signal it.
        container.FakeHttpMessageHandler.AddResponse(
            LocalEvaluationUrl,
            HttpMethod.Get,
            async () =>
            {
                pollStarted.SetResult(true);
                await pollCanProceed.Task;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        LocalEvaluationResponse,
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            });

        using var httpClient = new HttpClient(container.FakeHttpMessageHandler);
        using var apiClient = container.Activate<PostHogApiClient>(httpClient);
        await using var loader = container.Activate<LocalFeatureFlagsLoader>(apiClient);
        await loader.RefreshAsync(CancellationToken.None);

        // Advance past the poll interval so the background poll fires.
        container.FakeTimeProvider.Advance(TimeSpan.FromSeconds(31));

        Task disposeTask;
        try
        {
            Assert.Same(pollStarted.Task, await Task.WhenAny(pollStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))));
            disposeTask = loader.DisposeAsync().AsTask();
            Assert.False(disposeTask.IsCompleted);
        }
        finally
        {
            pollCanProceed.SetResult(true);
        }

        // Verify disposal completes without deadlock or exception.
        var timeout = TimeSpan.FromSeconds(5);
        var completed = await Task.WhenAny(disposeTask, Task.Delay(timeout));
        if (completed != disposeTask)
        {
            throw new TimeoutException("DisposeAsync did not complete within 5 seconds; possible deadlock.");
        }

        // Surface any exception thrown during disposal.
        await disposeTask;
    }

    [Fact]
    public async Task DoesNotDisposeTwice()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(LocalEvaluationResponse);

        var client = container.Activate<PostHogClient>();
        await client.LoadFeatureFlagsAsync(CancellationToken.None);

        await Task.WhenAll(
            client.DisposeAsync().AsTask(),
            client.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task CompletesGracefullyWhenPollingNeverStarted()
    {
        var container = new TestContainer();
        var client = container.Activate<PostHogClient>();

        // Dispose without ever calling LoadFeatureFlagsAsync.
        await client.DisposeAsync();
    }
}

public class TheUnresolvableFlagWarning
{
    const string CheckoutWithExperienceContinuity = """
        {"id": 2, "key": "checkout", "active": true, "ensure_experience_continuity": true,
         "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}}
        """;

    static string Flag(string key, string filter = "", bool experienceContinuity = false, int rolloutPercentage = 100) => $$$"""
        {"id": 1, "key": "{{{key}}}", "active": true, "ensure_experience_continuity": {{{(experienceContinuity ? "true" : "false")}}},
         "filters": {"groups": [{"properties": [{{{filter}}}], "rollout_percentage": {{{rolloutPercentage}}}}]}}
        """;

    static string Definitions(params string[] flags) => $$$"""{"flags": [{{{string.Join(", ", flags)}}}], "cohorts": {}}""";

    static (TestContainer Container, PostHogClient Client, FakeHttpMessageHandler.RequestHandler FlagsHandler) CreateClient()
    {
        var container = new TestContainer("fake-personal-api-key");
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        return (container, container.Activate<PostHogClient>(), flagsHandler);
    }

    static async Task LoadAsync(TestContainer container, PostHogClient client, string definitions)
    {
        var handler = container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(definitions, "\"definitions\"");
        await client.LoadFeatureFlagsAsync(CancellationToken.None);
        Assert.Single(handler.ReceivedRequests);
    }

    static List<string> GetWarnings(TestContainer container, string reason) =>
        container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning)
            .Select(e => e.Message ?? string.Empty)
            .Where(message => message.Contains($"(reason: {reason})", StringComparison.Ordinal))
            .ToList();

    [Theory]
    [InlineData("unchanged definitions")]
    [InlineData("304 not modified")]
    public async Task LoadingDefinitionThatLocalEvaluationNeverResolvesLogsOneWarning(string refreshResult)
    {
        var (container, client, flagsHandler) = CreateClient();
        var definitions = Definitions(
            CheckoutWithExperienceContinuity,
            Flag("static-cohort", """{"key": "id", "type": "cohort", "value": 999}"""),
            Flag("missing-dependency", """{"key": "missing", "type": "flag", "value": true, "operator": "flag_evaluates_to", "dependency_chain": ["missing"]}"""));
        await LoadAsync(container, client, definitions);

        if (refreshResult == "304 not modified")
        {
            var notModified = container.FakeHttpMessageHandler.AddLocalEvaluationNotModifiedResponse();
            await client.LoadFeatureFlagsAsync(CancellationToken.None);
            Assert.Single(notModified.ReceivedRequests);
        }
        else
        {
            await LoadAsync(container, client, definitions);
        }

        var warning = Assert.Single(GetWarnings(container, "experience_continuity"));
        Assert.Contains("checkout", warning, StringComparison.Ordinal);
        Assert.Single(GetWarnings(container, "unsupported_definition"));
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    static string WithRuntime(string flag, string? runtimeJson) => runtimeJson is null
        ? flag
        : flag.Insert(1, $"\"evaluation_runtime\": {runtimeJson}, ");

    static string RuntimeDefinitions(string? runtimeJson) => Definitions(
        WithRuntime(CheckoutWithExperienceContinuity, runtimeJson),
        WithRuntime(Flag("static-cohort", """{"key": "id", "type": "cohort", "value": 999}"""), runtimeJson));

    [Theory]
    [InlineData("\"client\"", 0)]
    [InlineData("\"server\"", 1)]
    [InlineData("\"all\"", 1)]
    [InlineData("null", 1)]
    [InlineData(null, 1)]
    [InlineData("\"Client\"", 1)]
    [InlineData("\"future\"", 1)]
    [InlineData("\"\"", 1)]
    public async Task RuntimeExcludesOnlyExactClientFromBothWarningReasons(string? runtimeJson, int expectedWarnings)
    {
        var (container, client, flagsHandler) = CreateClient();
        await using var disposableClient = client;
        var definitions = RuntimeDefinitions(runtimeJson);
        await LoadAsync(container, client, definitions);
        await LoadAsync(container, client, definitions);
        var notModified = container.FakeHttpMessageHandler.AddLocalEvaluationNotModifiedResponse();
        await client.LoadFeatureFlagsAsync(CancellationToken.None);

        Assert.Single(notModified.ReceivedRequests);
        Assert.Empty(flagsHandler.ReceivedRequests);
        foreach (var (reason, key) in new[] { ("experience_continuity", "checkout"), ("unsupported_definition", "static-cohort") })
        {
            var warnings = GetWarnings(container, reason);
            Assert.Equal(expectedWarnings, warnings.Count);
            Assert.All(warnings, warning => Assert.Contains($": {key}.", warning, StringComparison.Ordinal));
        }
        Assert.Equal(expectedWarnings * 2, container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning).Count);
    }

    [Theory]
    [InlineData("\"client\"", "client")]
    [InlineData("\"server\"", "server")]
    [InlineData("\"all\"", "all")]
    [InlineData("null", null)]
    [InlineData(null, null)]
    [InlineData("\"Client\"", "Client")]
    [InlineData("\"future\"", "future")]
    [InlineData("\"\"", "")]
    public void RuntimeMetadataSurvivesJsonDecodingAndParticipatesInRecordEquality(string? runtimeJson, string? expected)
    {
        var definitions = RuntimeDefinitions(runtimeJson);
        var decoded = JsonSerializer.Deserialize<LocalEvaluationApiResult>(definitions, JsonSerializerHelper.Options)!;
        var repeated = JsonSerializer.Deserialize<LocalEvaluationApiResult>(definitions, JsonSerializerHelper.Options)!;

        Assert.Equal(decoded, repeated);
        Assert.All(decoded.Flags, flag =>
        {
            Assert.Equal(expected, flag.EvaluationRuntime);
            Assert.Equal(flag, flag with { EvaluationRuntime = expected });
            Assert.Equal(flag.GetHashCode(), (flag with { EvaluationRuntime = expected }).GetHashCode());
            Assert.NotEqual(flag, flag with { EvaluationRuntime = "different-runtime" });
        });
    }

    [Fact]
    public async Task RuntimeMixedDefinitionsWarnWithOnlyEligibleCountsAndNames()
    {
        var (container, client, flagsHandler) = CreateClient();
        await using var disposableClient = client;
        var definitions = Definitions(
            WithRuntime(Flag("client-continuity", experienceContinuity: true), "\"client\""),
            WithRuntime(Flag("server-continuity", experienceContinuity: true), "\"server\""),
            WithRuntime(Flag("all-continuity", experienceContinuity: true), "\"all\""),
            WithRuntime(Flag("client-cohort", """{"key": "id", "type": "cohort", "value": 999}"""), "\"client\""),
            WithRuntime(Flag("null-cohort", """{"key": "id", "type": "cohort", "value": 999}"""), "null"),
            Flag("legacy-cohort", """{"key": "id", "type": "cohort", "value": 999}"""));
        await LoadAsync(container, client, definitions);
        await LoadAsync(container, client, definitions);

        var continuity = Assert.Single(GetWarnings(container, "experience_continuity"));
        Assert.Contains("2 flag(s)", continuity, StringComparison.Ordinal);
        Assert.Contains(": server-continuity, all-continuity.", continuity, StringComparison.Ordinal);
        var unsupported = Assert.Single(GetWarnings(container, "unsupported_definition"));
        Assert.Contains("2 flag(s)", unsupported, StringComparison.Ordinal);
        Assert.Contains(": null-cohort, legacy-cohort.", unsupported, StringComparison.Ordinal);
        Assert.DoesNotContain("client-", continuity, StringComparison.Ordinal);
        Assert.DoesNotContain("client-", unsupported, StringComparison.Ordinal);
        Assert.Equal(2, container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning).Count);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Theory]
    [InlineData("\"server\"")]
    [InlineData("\"all\"")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task RuntimeReturningFromClientToOriginalDefinitionWarnsAgain(string? runtimeJson)
    {
        var (container, client, flagsHandler) = CreateClient();
        await using var disposableClient = client;
        var original = RuntimeDefinitions(runtimeJson);
        await LoadAsync(container, client, original);
        await LoadAsync(container, client, original);
        await LoadAsync(container, client, RuntimeDefinitions("\"client\""));
        await LoadAsync(container, client, RuntimeDefinitions("\"client\""));
        Assert.Single(GetWarnings(container, "experience_continuity"));
        Assert.Single(GetWarnings(container, "unsupported_definition"));

        await LoadAsync(container, client, original);
        await LoadAsync(container, client, original);
        container.FakeHttpMessageHandler.AddLocalEvaluationNotModifiedResponse();
        await client.LoadFeatureFlagsAsync(CancellationToken.None);

        Assert.Equal(2, GetWarnings(container, "experience_continuity").Count);
        Assert.Equal(2, GetWarnings(container, "unsupported_definition").Count);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task RuntimeClientDefinitionsKeepLocalValuesAndUnresolvedReasons()
    {
        var (container, client, flagsHandler) = CreateClient();
        await using var disposableClient = client;
        await LoadAsync(container, client, Definitions(
            WithRuntime(Flag("client-flag"), "\"client\""),
            WithRuntime(CheckoutWithExperienceContinuity, "\"client\""),
            WithRuntime(Flag("static-cohort", """{"key": "id", "type": "cohort", "value": 999}"""), "\"client\"")));

        var snapshot = await client.EvaluateFlagsAsync(
            "user-123", new FeatureFlagOptions { OnlyEvaluateLocally = true }, CancellationToken.None);

        Assert.Equal("client-flag", Assert.Single(snapshot.Keys));
        Assert.True(snapshot.IsEnabled("client-flag"));
        Assert.Equal(2, snapshot.UnresolvedFlags.Count);
        Assert.Equal(UnresolvedFlagReason.ExperienceContinuity, snapshot.UnresolvedFlags["checkout"]);
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, snapshot.UnresolvedFlags["static-cohort"]);
        Assert.Empty(flagsHandler.ReceivedRequests);
        Assert.Empty(GetWarnings(container, "experience_continuity"));
        Assert.Empty(GetWarnings(container, "unsupported_definition"));
    }

    [Fact]
    public async Task DefinitionTheWarningCannotReadDoesNotBreakLoadingOrFlagCalls()
    {
        var (container, client, flagsHandler) = CreateClient();
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            Definitions(
                Flag("malformed", """{"key": null, "type": "flag", "value": true, "operator": "flag_evaluates_to", "dependency_chain": ["resolvable"]}"""),
                Flag("resolvable")),
            "\"definitions\"");

        var flags = await client.EvaluateFlagsAsync(
            "user-123",
            new FeatureFlagOptions { FlagKeysToEvaluate = ["resolvable"] },
            CancellationToken.None);

        Assert.True(flags.IsEnabled("resolvable"));
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task ChangedDefinitionThatLocalEvaluationNeverResolvesLogsTheWarningAgain()
    {
        var (container, client, _) = CreateClient();
        await LoadAsync(container, client, Definitions(Flag("checkout", experienceContinuity: true)));
        await LoadAsync(container, client, Definitions(Flag("checkout", experienceContinuity: true, rolloutPercentage: 50)));

        var warnings = GetWarnings(container, "experience_continuity");
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, warning => Assert.Contains("checkout", warning, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WarnsOncePerReasonAboutDefinitionCausesOnly()
    {
        var (container, client, _) = CreateClient();

        await LoadAsync(container, client, Definitions(
            Flag("experience-continuity", experienceContinuity: true),
            Flag("static-cohort", """{"key": "id", "type": "cohort", "value": 999}"""),
            Flag("unknown-operator", """{"key": "plan", "type": "person", "value": "pro", "operator": "operator_from_the_future"}"""),
            Flag("missing-dependency", """{"key": "missing", "type": "flag", "value": true, "operator": "flag_evaluates_to", "dependency_chain": ["missing"]}"""),
            Flag("circular-dependency", """{"key": "resolvable", "type": "flag", "value": true, "operator": "flag_evaluates_to", "dependency_chain": []}"""),
            Flag("unresolved-dependency", """{"key": "experience-continuity", "type": "flag", "value": true, "operator": "flag_evaluates_to", "dependency_chain": ["experience-continuity"]}"""),
            Flag("missing-context", """{"key": "plan", "type": "person", "value": "pro", "operator": "exact"}"""),
            Flag("resolvable")));

        var experienceContinuity = Assert.Single(GetWarnings(container, "experience_continuity"));
        Assert.Contains(": experience-continuity.", experienceContinuity, StringComparison.Ordinal);
        var unsupportedDefinition = Assert.Single(GetWarnings(container, "unsupported_definition"));
        Assert.Contains(
            ": static-cohort, unknown-operator, missing-dependency, circular-dependency.",
            unsupportedDefinition,
            StringComparison.Ordinal);
        Assert.Equal(2, container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning).Count);
    }

    [Fact]
    public async Task FlagThatLeavesAndReturnsLogsTheWarningAgain()
    {
        var (container, client, _) = CreateClient();
        await LoadAsync(container, client, Definitions(CheckoutWithExperienceContinuity, Flag("resolvable")));
        await LoadAsync(container, client, Definitions(Flag("resolvable")));
        await LoadAsync(container, client, Definitions(CheckoutWithExperienceContinuity, Flag("resolvable")));

        Assert.Equal(2, GetWarnings(container, "experience_continuity").Count);
    }

    [Fact]
    public async Task FlagThatBecomesResolvableAndThenUnresolvableLogsTheWarningAgain()
    {
        var (container, client, _) = CreateClient();
        await LoadAsync(container, client, Definitions(Flag("checkout", experienceContinuity: true)));
        await LoadAsync(container, client, Definitions(Flag("checkout")));
        await LoadAsync(container, client, Definitions(Flag("checkout", experienceContinuity: true)));

        Assert.Equal(2, GetWarnings(container, "experience_continuity").Count);
    }

    [Fact]
    public async Task ChangedReasonLogsTheNewReason()
    {
        var (container, client, _) = CreateClient();
        await LoadAsync(container, client, Definitions(Flag("checkout", experienceContinuity: true)));
        await LoadAsync(container, client, Definitions(Flag("checkout", """{"key": "id", "type": "cohort", "value": 999}""")));

        Assert.Single(GetWarnings(container, "experience_continuity"));
        Assert.Contains("checkout", Assert.Single(GetWarnings(container, "unsupported_definition")), StringComparison.Ordinal);
    }
}
