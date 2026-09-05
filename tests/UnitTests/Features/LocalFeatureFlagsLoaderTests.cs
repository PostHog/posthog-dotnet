using System.Net;
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
        var pollStarted = new TaskCompletionSource();
        var pollCanProceed = new TaskCompletionSource();

        // First response succeeds immediately (the initial load).
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(LocalEvaluationResponse);

        // Second response (the timer-triggered poll) blocks until we signal it.
        container.FakeHttpMessageHandler.AddResponse(
            LocalEvaluationUrl,
            HttpMethod.Get,
            async () =>
            {
                pollStarted.SetResult();
                await pollCanProceed.Task;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        LocalEvaluationResponse,
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            });

        var client = container.Activate<PostHogClient>();

        // Initial load starts the polling loop and makes the first API call.
        await client.LoadFeatureFlagsAsync(CancellationToken.None);

        // Advance past the poll interval so the background poll fires.
        container.FakeTimeProvider.Advance(TimeSpan.FromSeconds(31));

        // Wait for the poll's API call to begin.
        await pollStarted.Task;

        // Begin disposal while the poll is mid-flight.
        var disposeTask = client.DisposeAsync().AsTask();

        // Unblock the in-flight API call so the poll can finish.
        pollCanProceed.SetResult();

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
