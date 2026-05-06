using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using PostHog;
using PostHog.Features;
using UnitTests.Fakes;

namespace PostHogRequestContextMiddlewareTests;

public class ThePostHogRequestContextMiddleware
{
    [Fact]
    public async Task NullHttpContextDoesNotThrow()
    {
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("should not run"), Substitute.For<IPostHogClient>());

        await middleware.InvokeAsync(null);
    }

    [Fact]
    public void NullTracingExtractionInputsReturnEmptyContext()
    {
        var requestContext = PostHogTracingHeaders.Extract(null, null);

        Assert.Null(requestContext.DistinctId);
        Assert.Null(requestContext.SessionId);
        Assert.Empty(requestContext.Properties);
    }

    [Fact]
    public async Task AppliesRequestContextHeadersAndRequestMetadataToDownstreamCaptures()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var httpContext = CreateHttpContext();
        httpContext.Request.Headers["x-posthog-distinct-id"] = "frontend-user";
        httpContext.Request.Headers["x-posthog-session-id"] = "frontend-session";
        httpContext.Request.Headers[HeaderNames.UserAgent] = "TestAgent/1.0";

        var middleware = CreateMiddleware(
            context =>
            {
                Assert.Equal("frontend-user", PostHogContext.Current?.DistinctId);
                Assert.Equal("frontend-session", PostHogContext.Current?.SessionId);
                client.Capture("middleware-event");
                return Task.CompletedTask;
            },
            client);

        await middleware.InvokeAsync(httpContext);
        Assert.Null(PostHogContext.Current);
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var batchItem = document.RootElement.GetProperty("batch")[0];
        Assert.Equal("frontend-user", batchItem.GetProperty("distinct_id").GetString());
        var properties = batchItem.GetProperty("properties");
        Assert.Equal("frontend-session", properties.GetProperty("$session_id").GetString());
        Assert.Equal("https://example.com/api/test", properties.GetProperty("$current_url").GetString());
        Assert.Equal("POST", properties.GetProperty("$request_method").GetString());
        Assert.Equal("/api/test", properties.GetProperty("$request_path").GetString());
        Assert.Equal("TestAgent/1.0", properties.GetProperty("$user_agent").GetString());
        Assert.Equal("10.0.0.2", properties.GetProperty("$ip").GetString());
    }

    [Fact]
    public async Task CanDisableTracingHeaderCaptureWhilePreservingRequestMetadata()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var httpContext = CreateHttpContext();
        httpContext.Request.Headers[PostHogTracingHeaders.DistinctId] = "header-user";
        httpContext.Request.Headers[PostHogTracingHeaders.SessionId] = "header-session";
        httpContext.Request.Headers[HeaderNames.UserAgent] = "TestAgent/1.0";

        var middleware = CreateMiddleware(
            _ =>
            {
                Assert.Null(PostHogContext.Current?.DistinctId);
                Assert.Null(PostHogContext.Current?.SessionId);
                client.Capture("metadata-event");
                return Task.CompletedTask;
            },
            client,
            options => options.UseTracingHeaders = false);

        await middleware.InvokeAsync(httpContext);
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var batchItem = document.RootElement.GetProperty("batch")[0];
        Assert.NotEqual("header-user", batchItem.GetProperty("distinct_id").GetString());
        Assert.True(Guid.TryParse(batchItem.GetProperty("distinct_id").GetString(), out _));
        var properties = batchItem.GetProperty("properties");
        Assert.False(properties.GetProperty("$process_person_profile").GetBoolean());
        Assert.False(properties.TryGetProperty("$session_id", out _));
        Assert.Equal("/api/test", properties.GetProperty("$request_path").GetString());
        Assert.Equal("TestAgent/1.0", properties.GetProperty("$user_agent").GetString());
        Assert.Equal("10.0.0.2", properties.GetProperty("$ip").GetString());
    }

    [Fact]
    public async Task ExplicitCaptureValuesOverrideRequestContext()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var httpContext = CreateHttpContext();
        httpContext.Request.Headers[PostHogTracingHeaders.DistinctId] = "context-user";
        httpContext.Request.Headers[PostHogTracingHeaders.SessionId] = "context-session";

        var middleware = CreateMiddleware(
            _ =>
            {
                client.Capture(
                    "explicit-user",
                    "explicit-event",
                    new Dictionary<string, object> { ["$session_id"] = "explicit-session" });
                return Task.CompletedTask;
            },
            client);

        await middleware.InvokeAsync(httpContext);
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var batchItem = document.RootElement.GetProperty("batch")[0];
        Assert.Equal("explicit-user", batchItem.GetProperty("distinct_id").GetString());
        Assert.Equal("explicit-session", batchItem.GetProperty("properties").GetProperty("$session_id").GetString());
    }

    [Fact]
    public async Task MissingRequestContextHeadersCreatePersonlessCaptureWithoutSession()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var httpContext = CreateHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "authenticated-user")],
            authenticationType: "test"));

        var middleware = CreateMiddleware(
            _ =>
            {
                client.Capture("personless-event");
                return Task.CompletedTask;
            },
            client);

        await middleware.InvokeAsync(httpContext);
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var batchItem = document.RootElement.GetProperty("batch")[0];
        Assert.True(Guid.TryParse(batchItem.GetProperty("distinct_id").GetString(), out _));
        Assert.NotEqual("authenticated-user", batchItem.GetProperty("distinct_id").GetString());
        var properties = batchItem.GetProperty("properties");
        Assert.False(properties.GetProperty("$process_person_profile").GetBoolean());
        Assert.False(properties.TryGetProperty("$session_id", out _));
    }

    [Fact]
    public async Task SanitizesEmptyControlCharacterAndLongHeaderValues()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var httpContext = CreateHttpContext();
        httpContext.Request.Headers[PostHogTracingHeaders.DistinctId] = " \u0000\u0001\u0002 ";
        httpContext.Request.Headers[PostHogTracingHeaders.SessionId] = $"  {new string('s', 1200)}\u0000  ";

        var middleware = CreateMiddleware(
            _ =>
            {
                client.Capture("sanitized-event");
                return Task.CompletedTask;
            },
            client);

        await middleware.InvokeAsync(httpContext);
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var batchItem = document.RootElement.GetProperty("batch")[0];
        Assert.True(Guid.TryParse(batchItem.GetProperty("distinct_id").GetString(), out _));
        var properties = batchItem.GetProperty("properties");
        Assert.Equal(new string('s', 1000), properties.GetProperty("$session_id").GetString());
    }

    [Fact]
    public async Task ConcurrentRequestsDoNotLeakRequestContext()
    {
        var container = new TestContainer();
        var client = container.Activate<PostHogClient>();
        var results = new Dictionary<string, (string? DistinctId, string? SessionId)>();
        var gate = new object();

        var middleware = CreateMiddleware(
            async context =>
            {
                await Task.Delay(25);
                lock (gate)
                {
                    results[context.Request.Path.Value ?? string.Empty] = (
                        PostHogContext.Current?.DistinctId,
                        PostHogContext.Current?.SessionId);
                }
            },
            client);

        var first = CreateHttpContext(path: "/first");
        first.Request.Headers[PostHogTracingHeaders.DistinctId] = "user-a";
        first.Request.Headers[PostHogTracingHeaders.SessionId] = "session-a";
        var second = CreateHttpContext(path: "/second");
        second.Request.Headers[PostHogTracingHeaders.DistinctId] = "user-b";
        second.Request.Headers[PostHogTracingHeaders.SessionId] = "session-b";

        await Task.WhenAll(middleware.InvokeAsync(first), middleware.InvokeAsync(second));

        Assert.Equal(("user-a", "session-a"), results["/first"]);
        Assert.Equal(("user-b", "session-b"), results["/second"]);
        Assert.Null(PostHogContext.Current);
    }

    [Fact]
    public async Task CapturesUnhandledExceptionsWithRequestContextAndRethrows()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var httpContext = CreateHttpContext();
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Request.Headers[PostHogTracingHeaders.DistinctId] = "exception-user";
        httpContext.Request.Headers[PostHogTracingHeaders.SessionId] = "exception-session";
        httpContext.Request.Headers[HeaderNames.UserAgent] = "ExceptionAgent/1.0";

        var middleware = CreateMiddleware(
            _ => throw new InvalidOperationException("boom"),
            client,
            options => options.CaptureExceptions = true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(httpContext));
        Assert.Null(PostHogContext.Current);
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var batchItem = document.RootElement.GetProperty("batch")[0];
        Assert.Equal("$exception", batchItem.GetProperty("event").GetString());
        Assert.Equal("exception-user", batchItem.GetProperty("distinct_id").GetString());
        var properties = batchItem.GetProperty("properties");
        Assert.Equal("exception-session", properties.GetProperty("$session_id").GetString());
        Assert.Equal("https://example.com/api/test", properties.GetProperty("$current_url").GetString());
        Assert.Equal("ExceptionAgent/1.0", properties.GetProperty("$user_agent").GetString());
        Assert.Equal("10.0.0.2", properties.GetProperty("$ip").GetString());
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, properties.GetProperty("$response_status_code").GetInt32());
    }

    [Fact]
    public async Task DoesNotDeriveExceptionIdentityFromAspNetUserAutomatically()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var httpContext = CreateHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "server-user")],
            authenticationType: "test"));
        httpContext.Request.Headers[PostHogTracingHeaders.DistinctId] = "client-user";
        httpContext.Request.Headers[PostHogTracingHeaders.SessionId] = "client-session";

        var middleware = CreateMiddleware(
            _ => throw new InvalidOperationException("boom"),
            client,
            options => options.CaptureExceptions = true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(httpContext));
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var batchItem = document.RootElement.GetProperty("batch")[0];
        Assert.Equal("$exception", batchItem.GetProperty("event").GetString());
        Assert.Equal("client-user", batchItem.GetProperty("distinct_id").GetString());
        var properties = batchItem.GetProperty("properties");
        Assert.Equal("client-session", properties.GetProperty("$session_id").GetString());
        Assert.Contains("/person/client-user", properties.GetProperty("$exception_personURL").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotCaptureUnhandledExceptionsByDefault()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var httpContext = CreateHttpContext();

        var middleware = CreateMiddleware(
            _ => throw new InvalidOperationException("boom"),
            client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(httpContext));
        await client.FlushAsync();

        Assert.Empty(requestHandler.ReceivedRequests);
    }

    [Fact]
    public async Task PreservesOriginalExceptionWhenExceptionCaptureThrows()
    {
        var postHog = Substitute.For<IPostHogClient>();
        postHog.CaptureException(
                Arg.Any<Exception>(),
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<GroupCollection?>(),
                Arg.Any<FeatureFlagEvaluations?>(),
                Arg.Any<DateTimeOffset?>())
            .Returns(_ => throw new InvalidOperationException("capture failed"));

        var middleware = CreateMiddleware(
            _ => throw new NotSupportedException("original"),
            postHog,
            options => options.CaptureExceptions = true);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => middleware.InvokeAsync(CreateHttpContext()));
        Assert.Equal("original", exception.Message);
    }

    static PostHogRequestContextMiddleware CreateMiddleware(
        RequestDelegate next,
        IPostHogClient client,
        Action<PostHogRequestContextOptions>? configure = null)
    {
        var options = new PostHogRequestContextOptions();
        configure?.Invoke(options);
        return new PostHogRequestContextMiddleware(next, client, options);
    }

    static DefaultHttpContext CreateHttpContext(string path = "/api/test")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = new QueryString("?filter=1");
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
        return httpContext;
    }
}
