using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PostHog;
using UnitTests.Fakes;

namespace UnhandledExceptionCaptureTests;

public class TheRuntimeExceptionHandlers
{
    [Theory]
    [InlineData(true, "fatal")]
    [InlineData(false, "error")]
    public async Task CapturesUnhandledExceptionWithRuntimeMetadata(bool terminating, string expectedLevel)
    {
        var container = CreateContainer(enabled: true);
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        var message = UniqueMessage();

        client.OnUnhandledException(this, new UnhandledExceptionEventArgs(
            new InvalidOperationException(message), terminating));
        await client.FlushAsync();

        var properties = Assert.Single(GetExceptionEvents(requestHandler, message));
        Assert.Equal(expectedLevel, properties.GetProperty("$exception_level").GetString());
        Assert.Equal("dotnet.appdomain_unhandled_exception", properties.GetProperty("$exception_source").GetString());
        var mechanism = properties.GetProperty("$exception_list")[0].GetProperty("mechanism");
        Assert.False(mechanism.GetProperty("handled").GetBoolean());
        Assert.Equal("onuncaughtexception", mechanism.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CapturesUnobservedTaskExceptionOnceWithoutObservingIt()
    {
        var container = CreateContainer(enabled: true);
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        var message = UniqueMessage();
        var failure = new InvalidOperationException(message);
        var args = new UnobservedTaskExceptionEventArgs(new AggregateException(failure));

        client.OnUnobservedTaskException(this, args);
        // A second report of the same failure arrives in a new wrapper, as the runtime would deliver it.
        client.OnUnobservedTaskException(this, new UnobservedTaskExceptionEventArgs(new AggregateException(failure)));
        await client.FlushAsync();

        var properties = Assert.Single(GetExceptionEvents(requestHandler, message));
        Assert.Equal(
            "dotnet.task_scheduler_unobserved_task_exception",
            properties.GetProperty("$exception_source").GetString());
        Assert.Equal("error", properties.GetProperty("$exception_level").GetString());
        // The event reports the task's real failure rather than the runtime's AggregateException wrapper.
        Assert.Equal("System.InvalidOperationException", properties.GetProperty("$exception_type").GetString());
        var outermost = properties.GetProperty("$exception_list")[0];
        Assert.Equal("System.InvalidOperationException", outermost.GetProperty("type").GetString());
        var mechanism = outermost.GetProperty("mechanism");
        Assert.False(mechanism.GetProperty("handled").GetBoolean());
        Assert.Equal("task", mechanism.GetProperty("type").GetString());
        Assert.False(args.Observed);
    }

    [Fact]
    public async Task CapturesUnobservedTaskExceptionWithSeveralFailuresAsAggregate()
    {
        var container = CreateContainer(enabled: true);
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        var aggregate = new AggregateException(
            UniqueMessage(),
            new InvalidOperationException("first"),
            new NotSupportedException("second"));

        client.OnUnobservedTaskException(this, new UnobservedTaskExceptionEventArgs(aggregate));
        await client.FlushAsync();

        var properties = Assert.Single(GetExceptionEvents(requestHandler, aggregate.Message));
        var exceptions = properties.GetProperty("$exception_list");
        Assert.Equal(3, exceptions.GetArrayLength());
        Assert.Equal("System.AggregateException", exceptions[0].GetProperty("type").GetString());
        Assert.False(exceptions[0].GetProperty("mechanism").GetProperty("handled").GetBoolean());
        for (var i = 1; i < 3; i++)
        {
            // Nested entries are chained, and their handled state is unknown so it is omitted.
            var mechanism = exceptions[i].GetProperty("mechanism");
            Assert.Equal("chained", mechanism.GetProperty("type").GetString());
            Assert.False(mechanism.TryGetProperty("handled", out _));
        }
    }

    [Fact]
    public async Task ManualCaptureIsNotSuppressedByRuntimeDeduplication()
    {
        var container = CreateContainer(enabled: true);
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        var message = UniqueMessage();
        var exception = new InvalidOperationException(message);

        client.OnUnhandledException(this, new UnhandledExceptionEventArgs(exception, isTerminating: false));
        Assert.True(client.CaptureException(exception, "some-distinct-id"));
        Assert.True(client.CaptureException(exception, "some-distinct-id"));
        await client.FlushAsync();

        Assert.Equal(3, GetExceptionEvents(requestHandler, message).Count);
    }

    [Fact]
    public async Task ManualCaptureCannotSetExceptionSourceThroughProperties()
    {
        var container = CreateContainer(enabled: false);
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        var message = UniqueMessage();

        client.CaptureException(
            new InvalidOperationException(message),
            "some-distinct-id",
            new Dictionary<string, object> { ["$exception_source"] = "custom.source" });
        await client.FlushAsync();

        var properties = Assert.Single(GetExceptionEvents(requestHandler, message));
        Assert.False(properties.TryGetProperty("$exception_source", out _));
        var mechanism = properties.GetProperty("$exception_list")[0].GetProperty("mechanism");
        Assert.True(mechanism.GetProperty("handled").GetBoolean());
        Assert.Equal("generic", mechanism.GetProperty("type").GetString());
    }

    [Fact]
    public async Task TerminatingFlushDoesNotDependOnTheCrashingThreadsSynchronizationContext()
    {
        var container = CreateContainer(enabled: true);
        // The response completes asynchronously, so the flush has continuations that need somewhere to run.
        var requestHandler = container.FakeHttpMessageHandler.AddResponse(
            new Uri("https://us.i.posthog.com/batch"),
            HttpMethod.Post,
            async () =>
            {
                await Task.Delay(50);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":1}", Encoding.UTF8, "application/json")
                };
            });
        await using var client = container.Activate<PostHogClient>();
        var message = UniqueMessage();
        var previous = SynchronizationContext.Current;
        var blocked = new BlockedSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(blocked);
        try
        {
            // A flush that needs this context can never finish, so a sync-over-async flush would hang here.
            client.OnUnhandledException(this, new UnhandledExceptionEventArgs(
                new InvalidOperationException(message), isTerminating: true));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        // No further flush: the handler itself must have delivered the event before returning.
        Assert.Single(GetExceptionEvents(requestHandler, message));
        Assert.Equal(0, blocked.PostCount);
    }

    [Fact]
    public async Task DoesNotCaptureRuntimeExceptionsWhenOptionIsOff()
    {
        var container = CreateContainer(enabled: false);
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();

        client.OnUnhandledException(this, new UnhandledExceptionEventArgs(
            new InvalidOperationException("boom"), isTerminating: false));
        client.OnUnobservedTaskException(this, new UnobservedTaskExceptionEventArgs(
            new AggregateException(new InvalidOperationException("task failed"))));
        await client.FlushAsync();

        Assert.Empty(requestHandler.ReceivedRequests);
    }

    static string UniqueMessage() => $"boom {Guid.NewGuid()}";

    // Filters on a unique message: an enabled client listens to process-wide runtime events, so unrelated
    // unobserved task failures from tests running in parallel can land in the same batch.
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

    static TestContainer CreateContainer(bool enabled)
        => new(services => services.Configure<PostHogOptions>(options =>
            options.CaptureUnhandledExceptions = enabled));

    // Counts posted continuations without ever running them, like a thread that is blocked in the crash handler.
    sealed class BlockedSynchronizationContext : SynchronizationContext
    {
        int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _postCount);

        public override SynchronizationContext CreateCopy() => this;
    }
}
