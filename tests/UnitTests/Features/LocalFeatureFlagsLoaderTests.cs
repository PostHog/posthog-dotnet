using System.Net;
using System.Text;
using PostHog;
using UnitTests.Fakes;
#if NETCOREAPP3_1
using TestLibrary.Fakes.Polyfills;
#endif

namespace LocalFeatureFlagsLoaderTests;

public class TheGetFeatureFlagsForLocalEvaluationAsyncMethod
{
    const string SuccessResponse = """
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

    static HttpResponseMessage BadRequest()
        => new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

    static HttpResponseMessage QuotaLimited()
        => new(HttpStatusCode.PaymentRequired)
        {
            Content = new StringContent(
                """{ "type": "quota_limited", "detail": "quota", "code": "payment_required" }""",
                Encoding.UTF8,
                "application/json")
        };

    static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    static readonly AllFeatureFlagsOptions LocalOnly = new() { OnlyEvaluateLocally = true };

    [Fact]
    public async Task DoesNotRefetchOnEveryEvaluationAfterFailure()
    {
        var container = new TestContainer("fake-personal-api-key");
        var requestCount = 0;
        for (var i = 0; i < 5; i++)
        {
            container.FakeHttpMessageHandler.AddResponse(
                LocalEvaluationUrl,
                HttpMethod.Get,
                () =>
                {
                    Interlocked.Increment(ref requestCount);
                    return Task.FromResult(BadRequest());
                });
        }
        var client = container.Activate<PostHogClient>();

        for (var i = 0; i < 4; i++)
        {
            await client.GetAllFeatureFlagsAsync("user-1", LocalOnly, CancellationToken.None);
        }

        // Only the first evaluation reaches the API; the rest are served by the negative cache.
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task DoesNotRefetchOnEveryEvaluationAfterQuotaLimit()
    {
        var container = new TestContainer("fake-personal-api-key");
        var requestCount = 0;
        for (var i = 0; i < 5; i++)
        {
            container.FakeHttpMessageHandler.AddResponse(
                LocalEvaluationUrl,
                HttpMethod.Get,
                () =>
                {
                    Interlocked.Increment(ref requestCount);
                    return Task.FromResult(QuotaLimited());
                });
        }
        var client = container.Activate<PostHogClient>();

        for (var i = 0; i < 4; i++)
        {
            await client.GetAllFeatureFlagsAsync("user-1", LocalOnly, CancellationToken.None);
        }

        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task RetriesLoadAfterPollIntervalElapses()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddResponse(LocalEvaluationUrl, HttpMethod.Get, () => Task.FromResult(BadRequest()));
        container.FakeHttpMessageHandler.AddResponse(LocalEvaluationUrl, HttpMethod.Get, () => Task.FromResult(Ok(SuccessResponse)));
        var client = container.Activate<PostHogClient>();

        // First load fails; the negative cache holds within the poll interval.
        Assert.Empty(await client.GetAllFeatureFlagsAsync("user-1", LocalOnly, CancellationToken.None));
        Assert.Empty(await client.GetAllFeatureFlagsAsync("user-1", LocalOnly, CancellationToken.None));

        // Once the poll interval elapses the loader recovers and clears the failure marker.
        container.FakeTimeProvider.Advance(TimeSpan.FromSeconds(31));

        var flags = await client.GetAllFeatureFlagsAsync("user-1", LocalOnly, CancellationToken.None);
        Assert.True(flags.ContainsKey("test-flag"));
    }
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
