using System.Net;
using PostHog;
using UnitTests.Fakes;
#if NETCOREAPP3_1
using TestLibrary.Fakes.Polyfills;
#endif

namespace LocalFeatureFlagsLoaderTests;

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

    static readonly Uri LocalEvaluationUrl =
        new("https://us.i.posthog.com/api/feature_flag/local_evaluation?token=fake-project-api-key&send_cohorts");

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
