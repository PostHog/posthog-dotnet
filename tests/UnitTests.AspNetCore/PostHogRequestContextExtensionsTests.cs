using System.Text.Json;
using Microsoft.Extensions.Logging;
using PostHog;
using UnitTests.Fakes;

namespace PostHogRequestContextExtensionsTests;

public class ThePostHogRequestContextExtensions
{
    [Fact]
    public async Task EvaluateFlagsAsyncUsesCurrentRequestContextDistinctId()
    {
        var container = new TestContainer();
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"flag-a": true}}""");
        var client = container.Activate<PostHogClient>();

        using (PostHogContext.BeginScope(distinctId: "context-user", fresh: true))
        {
            await client.EvaluateFlagsAsync();
        }

        var request = flagsHandler.ReceivedRequests.Single();
        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("context-user", doc.RootElement.GetProperty("distinct_id").GetString());
    }

    [Fact]
    public async Task MissingContextDistinctIdLogsWarningOnceAndAvoidsHttpCalls()
    {
        var container = new TestContainer();
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"flag-a": true}}""");
        var client = container.Activate<PostHogClient>();

        var first = await client.EvaluateFlagsAsync();
        var second = await client.EvaluateFlagsAsync();

        Assert.Empty(first.Keys);
        Assert.Empty(second.Keys);
        Assert.Empty(flagsHandler.ReceivedRequests);
        var warning = Assert.Single(
            container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning),
            e => (e.Message ?? string.Empty).Contains("distinctId is required", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, warning.LogLevel);
    }
}
