using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog;
using PostHog.Features;
using UnitTests.Fakes;

namespace FeatureFlagEvaluationsTests;

public class TheEvaluateFlagsAsyncMethod
{
    [Fact]
    public async Task ReturnsSnapshotWithRichMetadataFromOneFlagsRequest()
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
                        "metadata": {"id": 42, "version": 7},
                        "reason": {"description": "matched condition set 1"}
                    },
                    "flag-b": {
                        "key": "flag-b",
                        "metadata": {"id": 43, "version": 2},
                        "reason": {"description": "variant assignment"}
                    }
                },
                "requestId": "the-request-id",
                "evaluatedAt": 1705862903000
            }
            """);
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);

        Assert.Equal(2, snapshot.Keys.Count);
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
            new AllFeatureFlagsOptions { OnlyEvaluateLocally = true },
            CancellationToken.None);

        Assert.True(snapshot.IsEnabled("flag-a"));
        Assert.Empty(flagsHandler.ReceivedRequests);
    }
}

public class TheSnapshotAccessMethods
{
    [Fact]
    public async Task IsEnabledReturnsFalseForUnknownKey()
    {
        var snapshot = await EvaluateAsync("""{"featureFlags": {"known": true}}""");
        Assert.False(snapshot.IsEnabled("unknown"));
    }

    [Fact]
    public async Task GetFlagReturnsNullForUnknownKey()
    {
        var snapshot = await EvaluateAsync("""{"featureFlags": {"known": true}}""");
        Assert.Null(snapshot.GetFlag("unknown"));
    }

    [Fact]
    public async Task GetFlagPayloadDoesNotFireFeatureFlagCalledEvent()
    {
        var (snapshot, batchHandler, client) = await EvaluateWithBatchAsync(
            """{"featureFlags": {"flag-a": true}, "featureFlagPayloads": {"flag-a": "\"hello\""}}""");

        var payload = snapshot.GetFlagPayload("flag-a");
        Assert.NotNull(payload);

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
                options.ProjectApiKey = "fake-project-api-key";
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
        Assert.Contains("\"$active_feature_flags\":[\"flag-a\",\"flag-c\"]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotMakeAdditionalFlagsHttpRequest()
    {
        var container = new TestContainer();
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"flag-a": true}}""");
        container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        client.Capture("user-1", "page_viewed", properties: null, groups: null, flags: snapshot);
        await client.FlushAsync();

        Assert.Single(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task SharesDedupCacheWithLegacySingleFlagPath()
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddRepeatedFlagsResponse(2, """{"featureFlags": {"flag-a": true}}""");
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        // Legacy path fires $feature_flag_called for ("user-1", "flag-a", true).
        Assert.True(await client.IsFeatureEnabledAsync("flag-a", "user-1"));

        // Snapshot path accesses the same flag — should hit the existing cache and NOT fire again.
        var snapshot = await client.EvaluateFlagsAsync("user-1", options: null, CancellationToken.None);
        snapshot.IsEnabled("flag-a");

        await client.FlushAsync();
        var body = batchHandler.GetReceivedRequestBody(indented: false);
        var matches = System.Text.RegularExpressions.Regex.Matches(body, "\\$feature_flag_called");
        Assert.Single(matches);
    }
}
