using System.Text.Json;
using PostHog;
using UnitTests.Fakes;

namespace PostHogContextTests;

public class ThePostHogContext
{
    [Fact]
    public void NestedScopesInheritUnlessFresh()
    {
        using (PostHogContext.BeginScope(
                   distinctId: "outer-user",
                   sessionId: "outer-session",
                   properties: new Dictionary<string, object> { ["outer"] = true },
                   fresh: true))
        {
            using (PostHogContext.BeginScope(properties: new Dictionary<string, object> { ["inner"] = true }))
            {
                Assert.Equal("outer-user", PostHogContext.Current?.DistinctId);
                Assert.Equal("outer-session", PostHogContext.Current?.SessionId);
                Assert.Equal(true, PostHogContext.Current?.Properties["outer"]);
                Assert.Equal(true, PostHogContext.Current?.Properties["inner"]);
            }

            using (PostHogContext.BeginScope(properties: new Dictionary<string, object> { ["fresh"] = true }, fresh: true))
            {
                Assert.Null(PostHogContext.Current?.DistinctId);
                Assert.Null(PostHogContext.Current?.SessionId);
                Assert.False(PostHogContext.Current?.Properties.ContainsKey("outer"));
                Assert.Equal(true, PostHogContext.Current?.Properties["fresh"]);
            }
        }

        Assert.Null(PostHogContext.Current);
    }

    [Fact]
    public async Task CaptureMergesContextWhenDistinctIdIsExplicit()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        using (PostHogContext.BeginScope(
                   distinctId: "context-user",
                   sessionId: "context-session",
                   properties: new Dictionary<string, object> { ["context-property"] = "context-value" },
                   fresh: true))
        {
            client.Capture("explicit-user", "context-event");
        }
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var batchItem = document.RootElement.GetProperty("batch")[0];
        Assert.Equal("explicit-user", batchItem.GetProperty("distinct_id").GetString());
        var properties = batchItem.GetProperty("properties");
        Assert.Equal("explicit-user", properties.GetProperty("distinct_id").GetString());
        Assert.Equal("context-session", properties.GetProperty("$session_id").GetString());
        Assert.Equal("context-value", properties.GetProperty("context-property").GetString());
        Assert.False(properties.TryGetProperty("$process_person_profile", out _));
    }

    [Fact]
    public async Task ExplicitCaptureValuesOverrideContext()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        using (PostHogContext.BeginScope(
                   distinctId: "context-user",
                   sessionId: "context-session",
                   properties: new Dictionary<string, object>
                   {
                       ["shared"] = "context-value",
                       ["context-only"] = "context-only-value"
                   },
                   fresh: true))
        {
            client.Capture(
                "explicit-user",
                "explicit-event",
                new Dictionary<string, object>
                {
                    ["shared"] = "explicit-value",
                    ["$session_id"] = "explicit-session"
                });
        }
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var batchItem = document.RootElement.GetProperty("batch")[0];
        Assert.Equal("explicit-user", batchItem.GetProperty("distinct_id").GetString());
        var properties = batchItem.GetProperty("properties");
        Assert.Equal("explicit-user", properties.GetProperty("distinct_id").GetString());
        Assert.Equal("explicit-session", properties.GetProperty("$session_id").GetString());
        Assert.Equal("explicit-value", properties.GetProperty("shared").GetString());
        Assert.Equal("context-only-value", properties.GetProperty("context-only").GetString());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(true, true)]
    public void PersonlessContextSetsProcessPersonProfile(bool? explicitOverride, bool expectedValue)
    {
        var properties = explicitOverride.HasValue
            ? new Dictionary<string, object> { ["$process_person_profile"] = explicitOverride.Value }
            : null;

        var context = PostHogContextHelper.ResolveCaptureContext(distinctId: null, properties: properties);

        Assert.True(Guid.TryParse(context.DistinctId, out _));
        Assert.True(context.IsPersonless);
        Assert.NotNull(context.Properties);
        Assert.Equal(expectedValue, (bool)context.Properties["$process_person_profile"]);
    }

    [Fact]
    public async Task CaptureExceptionMergesContextWhenDistinctIdIsExplicit()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        using (PostHogContext.BeginScope(distinctId: "context-user", sessionId: "context-session", fresh: true))
        {
            client.CaptureException(new InvalidOperationException("boom"), "explicit-user");
        }
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var batchItem = document.RootElement.GetProperty("batch")[0];
        Assert.Equal("$exception", batchItem.GetProperty("event").GetString());
        Assert.Equal("explicit-user", batchItem.GetProperty("distinct_id").GetString());
        var properties = batchItem.GetProperty("properties");
        Assert.Equal("context-session", properties.GetProperty("$session_id").GetString());
        Assert.Contains("/person/explicit-user", properties.GetProperty("$exception_personURL").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentAsyncScopesDoNotLeak()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        await Task.WhenAll(
            CaptureInContextAsync("user-a", "session-a", "event-a"),
            CaptureInContextAsync("user-b", "session-b", "event-b"));
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var events = document.RootElement.GetProperty("batch")
            .EnumerateArray()
            .ToDictionary(e => e.GetProperty("event").GetString()!);

        Assert.Equal("user-a", events["event-a"].GetProperty("distinct_id").GetString());
        Assert.Equal("session-a", events["event-a"].GetProperty("properties").GetProperty("$session_id").GetString());
        Assert.Equal("user-b", events["event-b"].GetProperty("distinct_id").GetString());
        Assert.Equal("session-b", events["event-b"].GetProperty("properties").GetProperty("$session_id").GetString());

        async Task CaptureInContextAsync(string distinctId, string sessionId, string eventName)
        {
            using (PostHogContext.BeginScope(distinctId: distinctId, sessionId: sessionId, fresh: true))
            {
                await Task.Delay(10);
                client.Capture(distinctId, eventName);
            }
        }
    }
}
