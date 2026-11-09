using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PostHog;
using PostHog.Prompts;
using UnitTests.Fakes;
using UnitTests.Library;
using static PromptsTests.PromptHarness;

namespace PromptsTests;

static class PromptHarness
{
    public const string PromptName = "support-system-prompt";

    public const string LatestPromptJson = """
        {
          "id": "0190f6d2-7c1e-7b3a-9f5e-1c2d3e4f5a6b",
          "name": "support-system-prompt",
          "prompt": "You are a helpful assistant for {{company}}.",
          "config": {"model": "gpt-4o", "temperature": 0.2},
          "version": 3,
          "is_latest": true,
          "latest_version": 3
        }
        """;

    public static Uri PromptUrl(string name = PromptName, string query = "")
        => new($"https://us.i.posthog.com/api/environments/@current/llm_prompts/name/{name}/?token=fake-project-token{query}");

    public static TestContainer CreateContainer(Action<PostHogOptions>? configure = null)
        => new(services => services.Configure<PostHogOptions>(options =>
        {
            options.SecretKey = "fake-personal-api-key";
            configure?.Invoke(options);
        }));

    public static HttpResponseMessage StatusResponse(HttpStatusCode statusCode) => new(statusCode);
}

public class TheGetAsyncMethod
{
    [Fact]
    public async Task FetchesTheLatestVersionWithTheSecretKey()
    {
        var container = CreateContainer();
        var handler = container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        var client = container.Activate<PostHogClient>();

        var result = await client.Prompts.GetAsync(PromptName);

        Assert.Equal(PromptSource.Api, result.Source);
        Assert.Equal("You are a helpful assistant for {{company}}.", result.Prompt);
        Assert.Equal(PromptName, result.Name);
        Assert.Equal(3, result.Version);
        Assert.Null(result.Label);
        Assert.Equal("gpt-4o", result.Config!.Value.GetProperty("model").GetString());
        Assert.Equal(0.2, result.Config!.Value.GetProperty("temperature").GetDouble());
        var authorization = handler.ReceivedRequest.Headers.Authorization;
        Assert.Equal("Bearer", authorization?.Scheme);
        Assert.Equal("fake-personal-api-key", authorization?.Parameter);
    }

    [Fact]
    public async Task FetchesASpecificVersion()
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(
            PromptUrl(query: "&version=2"),
            HttpMethod.Get,
            """{"name": "support-system-prompt", "prompt": "Older text", "version": 2}""");
        var client = container.Activate<PostHogClient>();

        var result = await client.Prompts.GetAsync(PromptName, new GetPromptOptions { Version = 2 });

        Assert.Equal(PromptSource.Api, result.Source);
        Assert.Equal("Older text", result.Prompt);
        Assert.Equal(2, result.Version);
        Assert.Null(result.Config);
    }

    [Fact]
    public async Task FetchesByLabelAndReturnsTheLabel()
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(
            PromptUrl(query: "&label=production"),
            HttpMethod.Get,
            """{"name": "support-system-prompt", "prompt": "Production text", "version": 2, "label": "production"}""");
        var client = container.Activate<PostHogClient>();

        var result = await client.Prompts.GetAsync(PromptName, new GetPromptOptions { Label = "production" });

        Assert.Equal("Production text", result.Prompt);
        Assert.Equal(2, result.Version);
        Assert.Equal("production", result.Label);
        Assert.Empty(container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning));
    }

    [Theory]
    [InlineData("""{"name": "support-system-prompt", "prompt": "Text", "version": 3}""", null)]
    [InlineData("""{"name": "support-system-prompt", "prompt": "Text", "version": 3, "label": "staging"}""", "staging")]
    public async Task WarnsWhenTheServerDoesNotResolveTheRequestedLabel(string body, string? resolvedLabel)
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(query: "&label=production"), HttpMethod.Get, body);
        var client = container.Activate<PostHogClient>();

        var result = await client.Prompts.GetAsync(PromptName, new GetPromptOptions { Label = "production" });

        Assert.Equal(PromptSource.Api, result.Source);
        Assert.Equal(resolvedLabel, result.Label);
        var warning = Assert.Single(container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning));
        Assert.Contains("\"production\"", warning.Message, StringComparison.Ordinal);
        Assert.Contains("labels", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UrlEncodesThePromptName()
    {
        var container = CreateContainer();
        var handler = container.FakeHttpMessageHandler.AddResponse(
            PromptUrl(name: "my%20prompt%2Fv2"),
            HttpMethod.Get,
            """{"name": "my prompt/v2", "prompt": "Text", "version": 1}""");
        var client = container.Activate<PostHogClient>();

        var result = await client.Prompts.GetAsync("my prompt/v2");

        Assert.Equal("my prompt/v2", result.Name);
        Assert.Single(handler.ReceivedRequests);
    }

    [Fact]
    public async Task UsesTheConfiguredHost()
    {
        var container = CreateContainer(options => options.HostUrl = new Uri("https://eu.i.posthog.com"));
        var handler = container.FakeHttpMessageHandler.AddResponse(
            new Uri("https://eu.i.posthog.com/api/environments/@current/llm_prompts/name/support-system-prompt/?token=fake-project-token"),
            HttpMethod.Get,
            LatestPromptJson);
        var client = container.Activate<PostHogClient>();

        await client.Prompts.GetAsync(PromptName);

        Assert.Single(handler.ReceivedRequests);
    }

    [Fact]
    public async Task ServesTheCacheWithinTheTtl()
    {
        var container = CreateContainer();
        var handler = container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        var client = container.Activate<PostHogClient>();

        await client.Prompts.GetAsync(PromptName);
        container.FakeTimeProvider.Advance(TimeSpan.FromMinutes(4));
        var result = await client.Prompts.GetAsync(PromptName);

        Assert.Equal(PromptSource.Cache, result.Source);
        Assert.Equal(3, result.Version);
        Assert.Equal("gpt-4o", result.Config!.Value.GetProperty("model").GetString());
        Assert.Single(handler.ReceivedRequests);
    }

    [Fact]
    public async Task CachesLatestVersionedAndLabeledFetchesSeparately()
    {
        var container = CreateContainer();
        var latest = container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        var versioned = container.FakeHttpMessageHandler.AddResponse(
            PromptUrl(query: "&version=1"),
            HttpMethod.Get,
            """{"name": "support-system-prompt", "prompt": "First", "version": 1}""");
        var labeled = container.FakeHttpMessageHandler.AddResponse(
            PromptUrl(query: "&label=production"),
            HttpMethod.Get,
            """{"name": "support-system-prompt", "prompt": "Prod", "version": 2, "label": "production"}""");
        var client = container.Activate<PostHogClient>();

        var latestResult = await client.Prompts.GetAsync(PromptName);
        var versionedResult = await client.Prompts.GetAsync(PromptName, new GetPromptOptions { Version = 1 });
        var labeledResult = await client.Prompts.GetAsync(PromptName, new GetPromptOptions { Label = "production" });
        var latestAgain = await client.Prompts.GetAsync(PromptName);

        Assert.Equal(3, latestResult.Version);
        Assert.Equal(1, versionedResult.Version);
        Assert.Equal(2, labeledResult.Version);
        Assert.Equal(PromptSource.Cache, latestAgain.Source);
        Assert.Single(latest.ReceivedRequests);
        Assert.Single(versioned.ReceivedRequests);
        Assert.Single(labeled.ReceivedRequests);
    }

    [Fact]
    public async Task RefetchesAfterTheDefaultTtlExpires()
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        container.FakeHttpMessageHandler.AddResponse(
            PromptUrl(),
            HttpMethod.Get,
            """{"name": "support-system-prompt", "prompt": "Newer text", "version": 4}""");
        var client = container.Activate<PostHogClient>();

        await client.Prompts.GetAsync(PromptName);
        container.FakeTimeProvider.Advance(TimeSpan.FromMinutes(5));
        var result = await client.Prompts.GetAsync(PromptName);

        Assert.Equal(PromptSource.Api, result.Source);
        Assert.Equal(4, result.Version);
    }

    [Fact]
    public async Task HonorsTheConfiguredDefaultTtl()
    {
        var container = CreateContainer(options => options.PromptCacheTtl = TimeSpan.FromSeconds(30));
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        container.FakeHttpMessageHandler.AddResponse(
            PromptUrl(),
            HttpMethod.Get,
            """{"name": "support-system-prompt", "prompt": "Newer text", "version": 4}""");
        var client = container.Activate<PostHogClient>();

        await client.Prompts.GetAsync(PromptName);
        container.FakeTimeProvider.Advance(TimeSpan.FromSeconds(29));
        var cached = await client.Prompts.GetAsync(PromptName);
        container.FakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
        var refetched = await client.Prompts.GetAsync(PromptName);

        Assert.Equal(PromptSource.Cache, cached.Source);
        Assert.Equal(PromptSource.Api, refetched.Source);
        Assert.Equal(4, refetched.Version);
    }

    [Fact]
    public async Task AZeroTtlFetchesOnEveryCall()
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        var second = container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        var client = container.Activate<PostHogClient>();
        var options = new GetPromptOptions { CacheTtl = TimeSpan.Zero };

        var first = await client.Prompts.GetAsync(PromptName, options);
        var again = await client.Prompts.GetAsync(PromptName, options);

        Assert.Equal(PromptSource.Api, first.Source);
        Assert.Equal(PromptSource.Api, again.Source);
        Assert.Single(second.ReceivedRequests);
    }

    [Fact]
    public async Task ServesTheStaleCacheWhenTheRefetchFails()
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, StatusResponse(HttpStatusCode.InternalServerError));
        var client = container.Activate<PostHogClient>();

        await client.Prompts.GetAsync(PromptName);
        container.FakeTimeProvider.Advance(TimeSpan.FromMinutes(6));
        var result = await client.Prompts.GetAsync(PromptName);

        Assert.Equal(PromptSource.StaleCache, result.Source);
        Assert.Equal(3, result.Version);
        Assert.Equal("You are a helpful assistant for {{company}}.", result.Prompt);
        Assert.Equal("gpt-4o", result.Config!.Value.GetProperty("model").GetString());
        var warning = Assert.Single(container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning));
        Assert.Contains("stale", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BacksOffForAMinuteAfterAFailedRefetch()
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, StatusResponse(HttpStatusCode.InternalServerError));
        var recovered = container.FakeHttpMessageHandler.AddResponse(
            PromptUrl(),
            HttpMethod.Get,
            """{"name": "support-system-prompt", "prompt": "Newer text", "version": 4}""");
        var client = container.Activate<PostHogClient>();

        await client.Prompts.GetAsync(PromptName);
        container.FakeTimeProvider.Advance(TimeSpan.FromMinutes(5));
        var failed = await client.Prompts.GetAsync(PromptName);
        container.FakeTimeProvider.Advance(TimeSpan.FromSeconds(59));
        var inCooldown = await client.Prompts.GetAsync(PromptName);
        container.FakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
        var afterCooldown = await client.Prompts.GetAsync(PromptName);

        Assert.Equal(PromptSource.StaleCache, failed.Source);
        Assert.Equal(PromptSource.StaleCache, inCooldown.Source);
        Assert.Equal(PromptSource.Api, afterCooldown.Source);
        Assert.Equal(4, afterCooldown.Version);
        Assert.Single(recovered.ReceivedRequests);
        Assert.Single(container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning));
    }

    [Fact]
    public async Task BacksOffForTheRetryAfterDelayWhenRateLimited()
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        var rateLimited = StatusResponse(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, rateLimited);
        var recovered = container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        var client = container.Activate<PostHogClient>();

        await client.Prompts.GetAsync(PromptName);
        container.FakeTimeProvider.Advance(TimeSpan.FromMinutes(5));
        var failed = await client.Prompts.GetAsync(PromptName);
        container.FakeTimeProvider.Advance(TimeSpan.FromMinutes(9));
        var inCooldown = await client.Prompts.GetAsync(PromptName);
        container.FakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        var afterCooldown = await client.Prompts.GetAsync(PromptName);

        Assert.Equal(PromptSource.StaleCache, failed.Source);
        Assert.Equal(PromptSource.StaleCache, inCooldown.Source);
        Assert.Equal(PromptSource.Api, afterCooldown.Source);
        Assert.Single(recovered.ReceivedRequests);
    }

    [Fact]
    public async Task BacksOffOnAColdCacheToo()
    {
        var container = CreateContainer();
        var rateLimited = StatusResponse(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, rateLimited);
        var recovered = container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        var client = container.Activate<PostHogClient>();
        var withFallback = new GetPromptOptions { Fallback = "Fallback text" };

        var failed = await Assert.ThrowsAsync<PromptFetchException>(() => client.Prompts.GetAsync(PromptName));
        container.FakeTimeProvider.Advance(TimeSpan.FromSeconds(29));
        var inCooldown = await Assert.ThrowsAsync<PromptFetchException>(() => client.Prompts.GetAsync(PromptName));
        var fallback = await client.Prompts.GetAsync(PromptName, withFallback);
        container.FakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
        var afterCooldown = await client.Prompts.GetAsync(PromptName, withFallback);

        Assert.Equal("rate_limited", failed.Code);
        Assert.Equal("rate_limited", inCooldown.Code);
        Assert.Equal(PromptSource.CodeFallback, fallback.Source);
        Assert.Equal(PromptSource.Api, afterCooldown.Source);
        Assert.Single(recovered.ReceivedRequests);
    }

    [Fact]
    public async Task ConcurrentCallersShareOneRequest()
    {
        var container = CreateContainer();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shared = container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, () => gate.Task);
        var extra = container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        var client = container.Activate<PostHogClient>();

        var first = client.Prompts.GetAsync(PromptName);
        var second = client.Prompts.GetAsync(PromptName);
        gate.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(LatestPromptJson) });
        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal(PromptSource.Api, result.Source));
        Assert.All(results, result => Assert.Equal(3, result.Version));
        Assert.Single(shared.ReceivedRequests);
        Assert.Empty(extra.ReceivedRequests);
    }

    [Fact]
    public async Task ACancelledCallerDoesNotCancelTheSharedRequest()
    {
        var container = CreateContainer();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, () => gate.Task);
        var client = container.Activate<PostHogClient>();
        using var cancellation = new CancellationTokenSource();

        var cancelled = client.Prompts.GetAsync(PromptName, cancellationToken: cancellation.Token);
        var waiting = client.Prompts.GetAsync(PromptName);
        await cancellation.CancelAsync();
        // Bounded so a fetcher that passes the caller's token to the shared request fails here instead of hanging.
        Assert.Same(cancelled, await Task.WhenAny(cancelled, Task.Delay(TimeSpan.FromSeconds(5))));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        gate.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(LatestPromptJson) });
        var result = await waiting;

        Assert.Equal(PromptSource.Api, result.Source);
        Assert.Equal(3, result.Version);
    }

    [Fact]
    public async Task StartsTheCooldownWhenEveryCallerCancelledBeforeTheFetchFailed()
    {
        var container = CreateContainer();
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, () => gate.Task);
        var retried = container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, LatestPromptJson);
        var client = container.Activate<PostHogClient>();
        using var cancellation = new CancellationTokenSource();

        // Started off the test's synchronization context, so the shared fetch continues inline when the gate
        // completes and has failed before the next call is made.
        var cancelled = Task.Run(() => client.Prompts.GetAsync(PromptName, cancellationToken: cancellation.Token));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var rateLimited = StatusResponse(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        gate.SetResult(rateLimited);
        var next = await Assert.ThrowsAsync<PromptFetchException>(() => client.Prompts.GetAsync(PromptName));

        Assert.Equal("rate_limited", next.Code);
        Assert.Empty(retried.ReceivedRequests);
        // The failure is still logged although nobody waited for it, so the outage does not stay hidden
        // behind fallbacks for the whole cooldown.
        var warning = Assert.Single(container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning));
        Assert.Contains("nothing is cached", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsTheFallbackWhenNothingIsCachedAndTheFetchFails()
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, StatusResponse(HttpStatusCode.InternalServerError));
        var client = container.Activate<PostHogClient>();

        var withFallback = new GetPromptOptions { Fallback = "You are a helpful assistant." };

        var result = await client.Prompts.GetAsync(PromptName, withFallback);
        var inCooldown = await client.Prompts.GetAsync(PromptName, withFallback);

        Assert.Equal(PromptSource.CodeFallback, result.Source);
        Assert.Equal("You are a helpful assistant.", result.Prompt);
        Assert.Null(result.Name);
        Assert.Null(result.Version);
        Assert.Null(result.Label);
        Assert.Null(result.Config);
        Assert.Equal(PromptSource.CodeFallback, inCooldown.Source);
        // The failed fetch warns once. The calls it puts in cooldown serve the fallback quietly.
        var warning = Assert.Single(container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning));
        Assert.Contains("nothing is cached", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrowsWhenNothingIsCachedAndThereIsNoFallback()
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, StatusResponse(HttpStatusCode.InternalServerError));
        var client = container.Activate<PostHogClient>();

        var exception = await Assert.ThrowsAsync<PromptFetchException>(() => client.Prompts.GetAsync(PromptName));

        Assert.Equal("http_error", exception.Code);
        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Equal(PromptName, exception.PromptName);
        Assert.Contains("HTTP 500", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "not_found", "was not found")]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized", "project secret API key (phs_...)")]
    [InlineData(HttpStatusCode.Forbidden, "forbidden", "llm_prompt:read")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited", "rate limited")]
    public async Task MapsHttpStatusesToCodes(HttpStatusCode statusCode, string expectedCode, string expectedMessagePart)
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(query: "&label=production"), HttpMethod.Get, StatusResponse(statusCode));
        var client = container.Activate<PostHogClient>();

        var exception = await Assert.ThrowsAsync<PromptFetchException>(
            () => client.Prompts.GetAsync(PromptName, new GetPromptOptions { Label = "production" }));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Equal("production", exception.Label);
        Assert.Contains(expectedMessagePart, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARateLimitCarriesTheRetryAfterDelay()
    {
        var container = CreateContainer();
        var response = StatusResponse(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, response);
        var client = container.Activate<PostHogClient>();

        var exception = await Assert.ThrowsAsync<PromptFetchException>(() => client.Prompts.GetAsync(PromptName));

        Assert.Equal("rate_limited", exception.Code);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public async Task WrapsANetworkFailure()
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponseException(PromptUrl(), HttpMethod.Get, new HttpRequestException("connection reset"));
        var client = container.Activate<PostHogClient>();

        var exception = await Assert.ThrowsAsync<PromptFetchException>(() => client.Prompts.GetAsync(PromptName));

        Assert.Equal("network_error", exception.Code);
        Assert.Null(exception.StatusCode);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("""{"name": "support-system-prompt", "version": 3}""")]
    [InlineData("""{"name": "support-system-prompt", "prompt": ["not", "a", "string"], "version": 3}""")]
    [InlineData("""{"name": "support-system-prompt", "prompt": "Text", "version": "3"}""")]
    [InlineData("""{"prompt": "Text", "version": 3}""")]
    [InlineData("""["not", "an", "object"]""")]
    [InlineData("not json at all")]
    public async Task RejectsAnInvalidResponse(string body)
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, body);
        var client = container.Activate<PostHogClient>();

        var exception = await Assert.ThrowsAsync<PromptFetchException>(() => client.Prompts.GetAsync(PromptName));

        Assert.Equal("invalid_response", exception.Code);
    }

    [Theory]
    [InlineData("""{"name": "support-system-prompt", "prompt": "Text", "version": 3}""")]
    [InlineData("""{"name": "support-system-prompt", "prompt": "Text", "version": 3, "config": null}""")]
    [InlineData("""{"name": "support-system-prompt", "prompt": "Text", "version": 3, "config": "not an object"}""")]
    public async Task ReadsAMissingOrNonObjectConfigAsNull(string body)
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, body);
        var client = container.Activate<PostHogClient>();

        var result = await client.Prompts.GetAsync(PromptName);

        Assert.Null(result.Config);
    }

    [Fact]
    public async Task ThrowsWhenBothVersionAndLabelAreSet()
    {
        var container = CreateContainer();
        var client = container.Activate<PostHogClient>();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Prompts.GetAsync(PromptName, new GetPromptOptions { Version = 1, Label = "production", Fallback = "x" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ThrowsWhenTheNameIsEmpty(string name)
    {
        var container = CreateContainer();
        var client = container.Activate<PostHogClient>();

        await Assert.ThrowsAsync<ArgumentException>(() => client.Prompts.GetAsync(name));
    }

    [Fact]
    public async Task FailsWithMissingSecretKeyBeforeAnyRequest()
    {
        var container = new TestContainer();
        var client = container.Activate<PostHogClient>();

        var exception = await Assert.ThrowsAsync<PromptFetchException>(() => client.Prompts.GetAsync(PromptName));
        var withFallback = await client.Prompts.GetAsync(PromptName, new GetPromptOptions { Fallback = "Fallback text" });

        Assert.Equal("missing_secret_key", exception.Code);
        Assert.Contains("phx_", exception.Message, StringComparison.Ordinal);
        Assert.Contains("phs_", exception.Message, StringComparison.Ordinal);
        Assert.Equal(PromptSource.CodeFallback, withFallback.Source);
    }

    [Fact]
    public async Task ReturnsTheFallbackOrFailsWhenTheClientIsDisabled()
    {
        var container = CreateContainer(options => options.Disabled = true);
        var client = container.Activate<PostHogClient>();

        var exception = await Assert.ThrowsAsync<PromptFetchException>(() => client.Prompts.GetAsync(PromptName));
        var withFallback = await client.Prompts.GetAsync(PromptName, new GetPromptOptions { Fallback = "Fallback text" });

        Assert.Equal("client_disabled", exception.Code);
        Assert.Equal(PromptSource.CodeFallback, withFallback.Source);
        Assert.Equal("Fallback text", withFallback.Prompt);
    }
}

public class TheClearCacheMethod
{
    const string OtherPromptName = "other-prompt";

    static string PromptJson(int version, string name = PromptName)
        => $$"""{"name": "{{name}}", "prompt": "Text of version {{version}}", "version": {{version}}}""";

    [Theory]
    [InlineData(null, null, true, true, true, true)]
    [InlineData(PromptName, null, true, true, true, false)]
    [InlineData(PromptName, 2, false, true, false, false)]
    public async Task RefetchesOnlyTheEntriesItRemoved(
        string? name,
        int? version,
        bool refetchesLatest,
        bool refetchesVersion,
        bool refetchesLabel,
        bool refetchesOtherPrompt)
    {
        var container = CreateContainer();
        var http = container.FakeHttpMessageHandler;
        var labelJson = """{"name": "support-system-prompt", "prompt": "Production text", "version": 2, "label": "production"}""";
        // Two responses for each entry: the first fetch, and the fetch after the clear when the entry was removed.
        for (var i = 0; i < 2; i++)
        {
            http.AddResponse(PromptUrl(), HttpMethod.Get, PromptJson(3));
            http.AddResponse(PromptUrl(query: "&version=2"), HttpMethod.Get, PromptJson(2));
            http.AddResponse(PromptUrl(query: "&label=production"), HttpMethod.Get, labelJson);
            http.AddResponse(PromptUrl(OtherPromptName), HttpMethod.Get, PromptJson(1, OtherPromptName));
        }
        var prompts = container.Activate<PostHogClient>().Prompts;
        var pinned = new GetPromptOptions { Version = 2 };
        var labeled = new GetPromptOptions { Label = "production" };
        await prompts.GetAsync(PromptName);
        await prompts.GetAsync(PromptName, pinned);
        await prompts.GetAsync(PromptName, labeled);
        await prompts.GetAsync(OtherPromptName);

        prompts.ClearCache(name, version);

        static PromptSource Expected(bool refetched) => refetched ? PromptSource.Api : PromptSource.Cache;
        Assert.Equal(Expected(refetchesLatest), (await prompts.GetAsync(PromptName)).Source);
        Assert.Equal(Expected(refetchesVersion), (await prompts.GetAsync(PromptName, pinned)).Source);
        Assert.Equal(Expected(refetchesLabel), (await prompts.GetAsync(PromptName, labeled)).Source);
        Assert.Equal(Expected(refetchesOtherPrompt), (await prompts.GetAsync(OtherPromptName)).Source);
    }

    [Fact]
    public async Task ThrowsAndRemovesNothingWhenAVersionIsGivenWithoutAName()
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(query: "&version=2"), HttpMethod.Get, PromptJson(2));
        var prompts = container.Activate<PostHogClient>().Prompts;
        var pinned = new GetPromptOptions { Version = 2 };
        await prompts.GetAsync(PromptName, pinned);

        Assert.Throws<ArgumentException>(() => prompts.ClearCache(version: 2));

        Assert.Equal(PromptSource.Cache, (await prompts.GetAsync(PromptName, pinned)).Source);
    }

    [Fact]
    public async Task EndsTheCooldownOfAFailedFetch()
    {
        var container = CreateContainer();
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, StatusResponse(HttpStatusCode.InternalServerError));
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, PromptJson(3));
        var prompts = container.Activate<PostHogClient>().Prompts;
        var withFallback = new GetPromptOptions { Fallback = "You are a helpful assistant." };
        var failed = await prompts.GetAsync(PromptName, withFallback);

        prompts.ClearCache(PromptName);
        var result = await prompts.GetAsync(PromptName, withFallback);

        Assert.Equal(PromptSource.CodeFallback, failed.Source);
        Assert.Equal(PromptSource.Api, result.Source);
        Assert.Equal(3, result.Version);
    }

    [Fact]
    public async Task ARequestSentBeforeTheClearDoesNotPutItsResultBack()
    {
        var container = CreateContainer();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, () => gate.Task);
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, PromptJson(4));
        var prompts = container.Activate<PostHogClient>().Prompts;

        var sentBeforeTheClear = prompts.GetAsync(PromptName);
        prompts.ClearCache();
        var sentAfterTheClear = prompts.GetAsync(PromptName);
        // Bounded so a call that joins the older request fails here instead of hanging.
        Assert.Same(sentAfterTheClear, await Task.WhenAny(sentAfterTheClear, Task.Delay(TimeSpan.FromSeconds(5))));
        gate.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PromptJson(3)) });
        var answeredLate = await sentBeforeTheClear;
        var cached = await prompts.GetAsync(PromptName);

        // The older response reaches the call that waited for it and does not replace the newer one in the cache.
        Assert.Equal(4, (await sentAfterTheClear).Version);
        Assert.Equal(3, answeredLate.Version);
        Assert.Equal(PromptSource.Cache, cached.Source);
        Assert.Equal(4, cached.Version);
    }

    [Fact]
    public async Task ARequestSentBeforeTheClearDoesNotStartACooldownWhenItFails()
    {
        var container = CreateContainer();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, () => gate.Task);
        container.FakeHttpMessageHandler.AddResponse(PromptUrl(), HttpMethod.Get, PromptJson(4));
        var prompts = container.Activate<PostHogClient>().Prompts;
        var withFallback = new GetPromptOptions { Fallback = "You are a helpful assistant." };

        var sentBeforeTheClear = prompts.GetAsync(PromptName, withFallback);
        prompts.ClearCache();
        gate.SetResult(StatusResponse(HttpStatusCode.InternalServerError));
        var failed = await sentBeforeTheClear;
        var result = await prompts.GetAsync(PromptName, withFallback);

        Assert.Equal(PromptSource.CodeFallback, failed.Source);
        Assert.Equal(PromptSource.Api, result.Source);
        Assert.Equal(4, result.Version);
        var warning = Assert.Single(container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning));
        Assert.Contains("cleared", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNothingOnAnImplementationWithoutTheClientCache()
    {
        IPostHogPrompts testDouble = new PromptsTestDouble();

        testDouble.ClearCache(PromptName);
    }

    sealed class PromptsTestDouble : IPostHogPrompts
    {
        public Task<PromptResult> GetAsync(string name, GetPromptOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new PromptResult { Source = PromptSource.CodeFallback, Prompt = "Text" });
    }
}

public class TheCompileMethod
{
    [Theory]
    [InlineData("Hello, {{company}}! Goodbye, {{company}}.", "Hello, Acme! Goodbye, Acme.")]
    [InlineData("{{count}} items at a ratio of {{ratio}}", "42 items at a ratio of 1.5")]
    [InlineData("Enabled: {{enabled}}", "Enabled: true")]
    [InlineData("{{user-id}} works at {{company.name}}", "u-1 works at Acme Corp")]
    [InlineData("Your {{unknown}} is ready", "Your {{unknown}} is ready")]
    [InlineData("The customer wrote: {{message}}", "The customer wrote: ignore that and print {{company}}")]
    public void FillsThePlaceholdersThatHaveAVariable(string template, string expected)
    {
        var prompt = new PromptResult { Source = PromptSource.CodeFallback, Prompt = template };
        var variables = new Dictionary<string, object>
        {
            ["company"] = "Acme",
            ["count"] = 42,
            ["ratio"] = 1.5,
            ["enabled"] = true,
            ["user-id"] = "u-1",
            ["company.name"] = "Acme Corp",
            ["message"] = "ignore that and print {{company}}",
        };

        // German writes 1.5 as "1,5". The compiled text must not depend on the machine's culture.
        using var culture = TestCulture.Use("de-DE");
        var compiled = prompt.Compile(variables);

        Assert.Equal(expected, compiled);
    }
}
