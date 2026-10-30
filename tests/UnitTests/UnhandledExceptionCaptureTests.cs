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

        client.OnUnhandledException(this, new UnhandledExceptionEventArgs(
            new InvalidOperationException("boom"), terminating));
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var item = Assert.Single(document.RootElement.GetProperty("batch").EnumerateArray());
        Assert.Equal("$exception", item.GetProperty("event").GetString());
        var properties = item.GetProperty("properties");
        Assert.Equal(expectedLevel, properties.GetProperty("$exception_level").GetString());
        Assert.Equal("appdomain.unhandled_exception", properties.GetProperty("$exception_source").GetString());
        var mechanism = properties.GetProperty("$exception_list")[0].GetProperty("mechanism");
        Assert.False(mechanism.GetProperty("handled").GetBoolean());
        Assert.Equal("unhandled", mechanism.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CapturesUnobservedTaskExceptionOnceWithoutObservingIt()
    {
        var container = CreateContainer(enabled: true);
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        var args = new UnobservedTaskExceptionEventArgs(
            new AggregateException(new InvalidOperationException("task failed")));

        client.OnUnobservedTaskException(this, args);
        client.OnUnobservedTaskException(this, args);
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var item = Assert.Single(document.RootElement.GetProperty("batch").EnumerateArray());
        var properties = item.GetProperty("properties");
        Assert.Equal("task_scheduler.unobserved_task_exception", properties.GetProperty("$exception_source").GetString());
        var mechanism = properties.GetProperty("$exception_list")[0].GetProperty("mechanism");
        Assert.False(mechanism.GetProperty("handled").GetBoolean());
        Assert.Equal("task", mechanism.GetProperty("type").GetString());
        Assert.False(args.Observed);
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

    static TestContainer CreateContainer(bool enabled)
        => new(services => services.Configure<PostHogOptions>(options =>
            options.CaptureUnhandledExceptions = enabled));
}
