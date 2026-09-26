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

            Assert.Equal("outer-user", PostHogContext.Current?.DistinctId);
            Assert.Equal("outer-session", PostHogContext.Current?.SessionId);
            Assert.Equal(new Dictionary<string, object> { ["outer"] = true }, PostHogContext.Current?.Properties);

            using (PostHogContext.BeginScope(properties: new Dictionary<string, object> { ["fresh"] = true }, fresh: true))
            {
                Assert.Null(PostHogContext.Current?.DistinctId);
                Assert.Null(PostHogContext.Current?.SessionId);
                Assert.False(PostHogContext.Current?.Properties.ContainsKey("outer"));
                Assert.Equal(true, PostHogContext.Current?.Properties["fresh"]);
            }

            Assert.Equal("outer-user", PostHogContext.Current?.DistinctId);
            Assert.Equal("outer-session", PostHogContext.Current?.SessionId);
            Assert.Equal(new Dictionary<string, object> { ["outer"] = true }, PostHogContext.Current?.Properties);
        }

        Assert.Null(PostHogContext.Current);
    }

    [Fact]
    public void InnerScopeDistinctIdOverridesParent()
    {
        using (PostHogContext.BeginScope(distinctId: "outer-user", fresh: true))
        using (PostHogContext.BeginScope(distinctId: "inner-user"))
        {
            Assert.Equal("inner-user", PostHogContext.Current?.DistinctId);
        }
    }

    [Fact]
    public void InnerScopeEmptyDistinctIdInheritsParent()
    {
        using (PostHogContext.BeginScope(distinctId: "outer-user", fresh: true))
        using (PostHogContext.BeginScope(distinctId: string.Empty))
        {
            Assert.Equal("outer-user", PostHogContext.Current?.DistinctId);
        }
    }

    [Fact]
    public void InnerScopeWhitespaceDistinctIdInheritsParent()
    {
        using (PostHogContext.BeginScope(distinctId: "outer-user", fresh: true))
        using (PostHogContext.BeginScope(distinctId: "   "))
        {
            Assert.Equal("outer-user", PostHogContext.Current?.DistinctId);
        }
    }

    [Fact]
    public void FreshScopeWhitespaceDistinctIdIsIgnored()
    {
        using (PostHogContext.BeginScope(distinctId: "   ", fresh: true))
        {
            Assert.Null(PostHogContext.Current?.DistinctId);
            var context = PostHogContextHelper.ResolveCaptureContext(distinctId: null, properties: null);
            Assert.True(context.IsPersonless);
        }
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
    [InlineData(false, false)]
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

        var readyA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var taskA = CaptureInContextAsync("user-a", "session-a", "event-a", readyA);
        var taskB = CaptureInContextAsync("user-b", "session-b", "event-b", readyB);
        try
        {
            var bothReady = Task.WhenAll(readyA.Task, readyB.Task);
            Assert.Same(bothReady, await Task.WhenAny(bothReady, Task.Delay(TimeSpan.FromSeconds(5))));
            await bothReady;
            Assert.Null(PostHogContext.Current);
        }
        finally
        {
            release.SetResult(true);
            await Task.WhenAll(taskA, taskB);
        }
        Assert.Null(PostHogContext.Current);
        await client.FlushAsync();

        using var document = JsonDocument.Parse(requestHandler.GetReceivedRequestBody(indented: false));
        var events = document.RootElement.GetProperty("batch")
            .EnumerateArray()
            .ToDictionary(e => e.GetProperty("event").GetString()!);

        Assert.Equal(2, events.Count);
        Assert.Equal("user-a", events["event-a"].GetProperty("distinct_id").GetString());
        Assert.Equal("session-a", events["event-a"].GetProperty("properties").GetProperty("$session_id").GetString());
        Assert.Equal("user-b", events["event-b"].GetProperty("distinct_id").GetString());
        Assert.Equal("session-b", events["event-b"].GetProperty("properties").GetProperty("$session_id").GetString());

        async Task CaptureInContextAsync(string distinctId, string sessionId, string eventName, TaskCompletionSource<bool> ready)
        {
            using (PostHogContext.BeginScope(distinctId: distinctId, sessionId: sessionId, fresh: true))
            {
                ready.SetResult(true);
                await release.Task;
                Assert.Equal(distinctId, PostHogContext.Current?.DistinctId);
                Assert.Equal(sessionId, PostHogContext.Current?.SessionId);
                client.Capture(null!, eventName);
            }
        }
    }
}
