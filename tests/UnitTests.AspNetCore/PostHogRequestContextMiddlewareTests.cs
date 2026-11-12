using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using PostHog;
using PostHog.AspNetCore;
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
    public void NullApplicationBuilderThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => PostHogRequestContextMiddlewareExtensions.UsePostHogRequestContext(null!));
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
    public async Task CurrentUrlOmitsQueryString()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var httpContext = CreateHttpContext();
        httpContext.Request.QueryString = new QueryString("?oauth_code=secret&filter=1");
        httpContext.Request.Headers[PostHogTracingHeaders.DistinctId] = "frontend-user";

        var middleware = CreateMiddleware(
            _ =>
            {
                client.Capture("current-url-event");
                return Task.CompletedTask;
            },
            client);

        await middleware.InvokeAsync(httpContext);
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var properties = document.RootElement.GetProperty("batch")[0].GetProperty("properties");
        Assert.Equal("https://example.com/api/test", properties.GetProperty("$current_url").GetString());
    }

    [Fact]
    public async Task OmitsCurrentUrlAndIpWhenRequestPartsAreMissing()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[PostHogTracingHeaders.DistinctId] = "frontend-user";

        var middleware = CreateMiddleware(
            _ =>
            {
                client.Capture("bare-context-event");
                return Task.CompletedTask;
            },
            client);

        await middleware.InvokeAsync(httpContext);
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var properties = document.RootElement.GetProperty("batch")[0].GetProperty("properties");
        Assert.False(properties.TryGetProperty("$current_url", out _));
        Assert.False(properties.TryGetProperty("$ip", out _));
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
        var bothEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredCount = 0;

        var middleware = CreateMiddleware(
            async context =>
            {
                if (Interlocked.Increment(ref enteredCount) == 2)
                {
                    bothEntered.SetResult(true);
                }
                await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
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

    [Theory]
    [InlineData(200, 500)]
    [InlineData(399, 500)]
    [InlineData(400, 400)]
    [InlineData(503, 503)]
    public async Task CapturesUnhandledExceptionsWithRequestContextAndRethrows(
        int responseStatusCode,
        int expectedCapturedStatusCode)
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        var httpContext = CreateHttpContext();
        httpContext.Response.StatusCode = responseStatusCode;
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
        Assert.Equal(expectedCapturedStatusCode, properties.GetProperty("$response_status_code").GetInt32());
        Assert.Equal("aspnetcore.request_context_middleware", properties.GetProperty("$exception_source").GetString());
        var mechanism = properties.GetProperty("$exception_list")[0].GetProperty("mechanism");
        Assert.False(mechanism.GetProperty("handled").GetBoolean());
        Assert.Equal("middleware", mechanism.GetProperty("type").GetString());
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
    public async Task DoesNotDuplicateExceptionWhenRequestFailureReachesRuntimeHandlers()
    {
        var container = new TestContainer(services => services.Configure<PostHogOptions>(options =>
            options.CaptureUnhandledExceptions = true));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        var message = $"boom {Guid.NewGuid()}";
        var original = new InvalidOperationException(message);
        var middleware = CreateMiddleware(
            _ => throw original,
            client,
            options => options.CaptureExceptions = true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(CreateHttpContext()));
        client.OnUnhandledException(this, new UnhandledExceptionEventArgs(original, isTerminating: false));
        // The runtime wraps an unobserved task failure in a new AggregateException each time.
        client.OnUnobservedTaskException(this, new UnobservedTaskExceptionEventArgs(new AggregateException(original)));
        await client.FlushAsync();

        Assert.Single(GetExceptionEvents(requestHandler, message));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DoesNotDuplicateAggregateWhenItLaterSurfacesAsUnobservedTaskFailure(int memberCount)
    {
        var container = new TestContainer(services => services.Configure<PostHogOptions>(options =>
            options.CaptureUnhandledExceptions = true));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        var message = $"aggregate {Guid.NewGuid()}";
        var failure = new AggregateException(
            message,
            Enumerable.Range(0, memberCount).Select(i => new InvalidOperationException($"{message} member {i}")));
        var middleware = CreateMiddleware(
            _ => throw failure,
            client,
            options => options.CaptureExceptions = true);
        await Assert.ThrowsAsync<AggregateException>(() => middleware.InvokeAsync(CreateHttpContext()));

        // Go through the real finalizer event: the runtime wraps the aggregate in a new AggregateException.
        var raised = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> probe = (_, args) =>
        {
            if (args.Exception.InnerExceptions.Any(inner => ReferenceEquals(inner, failure)))
            {
                Interlocked.Increment(ref raised);
            }
        };
        TaskScheduler.UnobservedTaskException += probe;
        try
        {
            LeaveUnobservedTask(failure);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Assert.Equal(1, Volatile.Read(ref raised));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= probe;
        }

        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var reports = document.RootElement.GetProperty("batch").EnumerateArray()
            .Where(item => item.GetProperty("properties").GetProperty("$exception_list").EnumerateArray()
                .Any(entry => entry.GetProperty("value").GetString()!.StartsWith(message, StringComparison.Ordinal)))
            .ToArray();
        var report = Assert.Single(reports);
        Assert.Equal(
            "System.AggregateException",
            report.GetProperty("properties").GetProperty("$exception_type").GetString());
    }

    // The services UseExceptionHandler needs. The provider owns and disposes the listener.
    static ServiceProvider CreateExceptionHandlerServices()
        => new ServiceCollection()
            .AddLogging()
            .AddMetrics()
            .AddSingleton(_ => new System.Diagnostics.DiagnosticListener("PostHogRequestContextMiddlewareTests"))
            .BuildServiceProvider();

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void LeaveUnobservedTask(Exception failure) => _ = Task.FromException(failure);

    [Fact]
    public async Task CapturesExceptionConsumedByDownstreamExceptionHandlerAsHandled()
    {
        var container = new TestContainer(services => services.Configure<PostHogOptions>(options =>
            options.CaptureUnhandledExceptions = true));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        var message = $"boom {Guid.NewGuid()}";

        // PostHog middleware wraps UseExceptionHandler, which wraps a throwing endpoint.
        using var pipelineServices = CreateExceptionHandlerServices();
        var app = new ApplicationBuilder(pipelineServices);
        app.UseExceptionHandler(errorApp => errorApp.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Task.CompletedTask;
        }));
        app.Run(_ => throw new InvalidOperationException(message));
        var middleware = CreateMiddleware(
            app.Build(),
            client,
            options => options.CaptureExceptions = true);
        var httpContext = CreateHttpContext();
        httpContext.RequestServices = pipelineServices;
        httpContext.Request.Headers[PostHogTracingHeaders.DistinctId] = "exception-user";
        httpContext.Request.Headers[PostHogTracingHeaders.SessionId] = "exception-session";

        // The handler consumed the exception, so nothing escapes the PostHog middleware.
        await middleware.InvokeAsync(httpContext);
        await client.FlushAsync();

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, httpContext.Response.StatusCode);
        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var batchItem = Assert.Single(
            document.RootElement.GetProperty("batch").EnumerateArray(),
            item => item.GetProperty("properties").GetProperty("$exception_list")[0]
                .GetProperty("value").GetString() == message);
        Assert.Equal("$exception", batchItem.GetProperty("event").GetString());
        Assert.Equal("exception-user", batchItem.GetProperty("distinct_id").GetString());
        var properties = batchItem.GetProperty("properties");
        Assert.Equal("exception-session", properties.GetProperty("$session_id").GetString());
        Assert.Equal("https://example.com/api/test", properties.GetProperty("$current_url").GetString());
        Assert.Equal(503, properties.GetProperty("$response_status_code").GetInt32());
        Assert.Equal("error", properties.GetProperty("$exception_level").GetString());
        Assert.Equal("aspnetcore.request_context_middleware", properties.GetProperty("$exception_source").GetString());
        var mechanism = properties.GetProperty("$exception_list")[0].GetProperty("mechanism");
        Assert.True(mechanism.GetProperty("handled").GetBoolean());
        Assert.Equal("middleware", mechanism.GetProperty("type").GetString());
    }

    [Fact]
    public async Task DoesNotCaptureConsumedExceptionWhenExceptionCaptureIsOff()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        using var pipelineServices = CreateExceptionHandlerServices();
        var app = new ApplicationBuilder(pipelineServices);
        app.UseExceptionHandler(errorApp => errorApp.Run(_ => Task.CompletedTask));
        app.Run(_ => throw new InvalidOperationException("boom"));
        var middleware = CreateMiddleware(app.Build(), client);
        var httpContext = CreateHttpContext();
        httpContext.RequestServices = pipelineServices;

        await middleware.InvokeAsync(httpContext);
        await client.FlushAsync();

        Assert.Empty(requestHandler.ReceivedRequests);
    }

    [Fact]
    public async Task CapturesSameExceptionInstanceForEveryFailingRequest()
    {
        // An app can rethrow one cached exception instance (a cached faulted task, Lazy<T>) on every request.
        var container = new TestContainer(services => services.Configure<PostHogOptions>(options =>
            options.CaptureUnhandledExceptions = true));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        var message = $"boom {Guid.NewGuid()}";
        var cached = new InvalidOperationException(message);
        var middleware = CreateMiddleware(
            _ => throw cached,
            client,
            options => options.CaptureExceptions = true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(CreateHttpContext()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(CreateHttpContext()));
        await client.FlushAsync();

        Assert.Equal(2, GetExceptionEvents(requestHandler, message).Count);
    }

    [Fact]
    public async Task ReportsUnhandledMetadataThroughWrappedClient()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        var wrapper = Substitute.For<IPostHogClient>();
        wrapper.CaptureException(
                Arg.Any<Exception>(),
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<GroupCollection?>(),
                Arg.Any<FeatureFlagEvaluations?>(),
                Arg.Any<DateTimeOffset?>())
            .Returns(call => client.CaptureException(
                call.ArgAt<Exception>(0),
                call.ArgAt<string>(1),
                call.ArgAt<Dictionary<string, object>?>(2),
                call.ArgAt<GroupCollection?>(3),
                call.ArgAt<FeatureFlagEvaluations?>(4),
                call.ArgAt<DateTimeOffset?>(5)));
        var message = $"boom {Guid.NewGuid()}";
        var middleware = CreateMiddleware(
            _ => throw new InvalidOperationException(message),
            wrapper,
            options => options.CaptureExceptions = true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(CreateHttpContext()));
        await client.FlushAsync();

        var properties = Assert.Single(GetExceptionEvents(requestHandler, message));
        Assert.Equal("aspnetcore.request_context_middleware", properties.GetProperty("$exception_source").GetString());
        var mechanism = properties.GetProperty("$exception_list")[0].GetProperty("mechanism");
        Assert.False(mechanism.GetProperty("handled").GetBoolean());
        Assert.Equal("middleware", mechanism.GetProperty("type").GetString());
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

        var original = new NotSupportedException("original");
        var middleware = CreateMiddleware(
            _ => throw original,
            postHog,
            options => options.CaptureExceptions = true);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => middleware.InvokeAsync(CreateHttpContext()));
        Assert.Same(original, exception);
        postHog.Received(1).CaptureException(
            original,
            Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>?>(properties => properties != null
                && (int)properties["$response_status_code"] == 500),
            Arg.Any<GroupCollection?>(),
            Arg.Any<FeatureFlagEvaluations?>(),
            Arg.Any<DateTimeOffset?>());
    }

    // Filters on a unique message: a client with runtime capture enabled listens to process-wide events, so
    // unrelated failures from tests running in parallel can land in the same batch.
    static List<JsonElement> GetExceptionEvents(
        FakeHttpMessageHandler.RequestHandler requestHandler,
        string message)
    {
        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        return document.RootElement.GetProperty("batch").EnumerateArray()
            .Select(item => item.GetProperty("properties"))
            .Where(properties => properties.GetProperty("$exception_list")[0].GetProperty("value").GetString() == message)
            .Select(properties => properties.Clone())
            .ToList();
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
