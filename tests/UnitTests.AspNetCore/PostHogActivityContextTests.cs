using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PostHog;
using PostHog.Config;

namespace PostHogActivityContextTests;

public class TheRequestActivityContext
{
    [Theory]
    [InlineData("browser-user", "browser-session", true, "browser-user", "browser-session")]
    [InlineData(null, "browser-session", true, null, "browser-session")]
    [InlineData(null, null, true, null, null)]
    [InlineData(" \u0000 ", " ", true, null, null)]
    [InlineData("browser-user", "browser-session", false, null, null)]
    public async Task AddsAvailableContextToExistingRequestAndChildActivities(
        string? distinctId,
        string? sessionId,
        bool useTracingHeaders,
        string? expectedDistinctId,
        string? expectedSessionId)
    {
        using var provider = CreateServices();
        using var source = new ActivitySource(Guid.NewGuid().ToString());
        var exported = new ConcurrentQueue<Dictionary<string, object?>>();
        using var exporter = Listen(source, activity => exported.Enqueue(activity.TagObjects.ToDictionary()));
        using var request = new Activity("request");
        request.Start();
        Activity? child = null;
        var pipeline = CreatePipeline(provider, async _ =>
        {
            await Task.Yield();
            child = source.StartActivity("child");
            Assert.NotNull(child);
            Assert.Equal(expectedDistinctId, child.GetTagItem("posthogDistinctId"));
            Assert.Equal(expectedSessionId, child.GetTagItem("sessionId"));
            using var nativeChild = new Activity("native-child");
            nativeChild.Start();
            Assert.Equal(expectedDistinctId, nativeChild.GetTagItem("posthogDistinctId"));
            Assert.Equal(expectedSessionId, nativeChild.GetTagItem("sessionId"));
        }, options => options.UseTracingHeaders = useTracingHeaders);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-POSTHOG-DISTINCT-ID"] = distinctId;
        context.Request.Headers["X-POSTHOG-SESSION-ID"] = sessionId;

        await pipeline(context);
        Assert.Null(PostHogContext.Current);
        Assert.Equal(expectedDistinctId, request.GetTagItem("posthogDistinctId"));
        Assert.Equal(expectedSessionId, request.GetTagItem("sessionId"));
        child!.Stop();

        var attributes = Assert.Single(exported);
        Assert.Equal(expectedDistinctId, attributes.GetValueOrDefault("posthogDistinctId"));
        Assert.Equal(expectedSessionId, attributes.GetValueOrDefault("sessionId"));
        Assert.DoesNotContain("$session_id", attributes.Keys);
        using var outside = source.StartActivity("outside");
        Assert.Null(outside!.GetTagItem("sessionId"));
        Assert.Null(outside.GetTagItem("posthogDistinctId"));
    }

    [Theory]
    [InlineData(true, "phc_test", false)]
    [InlineData(false, "", false)]
    [InlineData(false, " ", false)]
    [InlineData(false, "phc_test", true)]
    public async Task RespectsTheOwningClientsDisabledState(bool disabled, string key, bool expectedEnrichment)
    {
        var options = new PostHogOptions { ProjectToken = key, Disabled = disabled };
        using var client = new PostHogClient(Microsoft.Extensions.Options.Options.Create(options));
        options.Disabled = !disabled;
        var services = new ServiceCollection();
        new PostHogConfigurationBuilder(services).UseAspNetCore();
        services.AddSingleton<IPostHogClient>(client);
        using var provider = services.BuildServiceProvider();
        using var source = new ActivitySource(Guid.NewGuid().ToString());
        using var exporter = Listen(source);
        using var request = new Activity("request");
        request.SetTag("app.key", "keep");
        request.Start();
        var pipeline = CreatePipeline(provider, _ =>
        {
            using var child = source.StartActivity("child");
            Assert.NotNull(child);
            Assert.Equal(expectedEnrichment ? "user" : null, child.GetTagItem("posthogDistinctId"));
            Assert.Equal(expectedEnrichment ? "session" : null, child.GetTagItem("sessionId"));
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Headers["X-POSTHOG-DISTINCT-ID"] = "user";
        context.Request.Headers["X-POSTHOG-SESSION-ID"] = "session";
        await pipeline(context);
        Assert.Equal(expectedEnrichment ? "user" : null, request.GetTagItem("posthogDistinctId"));
        Assert.Equal(expectedEnrichment ? "session" : null, request.GetTagItem("sessionId"));
        Assert.Equal("keep", request.GetTagItem("app.key"));
        Assert.Null(PostHogContext.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreservesExplicitAttributesAndCleansUpAfterSuccessOrFailure(bool fail)
    {
        using var provider = CreateServices();
        using var source = new ActivitySource(Guid.NewGuid().ToString());
        using var exporter = Listen(source);
        using var request = new Activity("request");
        request.SetTag("posthogDistinctId", "server-user");
        request.SetTag("sessionId", "server-session");
        request.Start();
        var pipeline = CreatePipeline(provider, _ =>
        {
            using var child = source.StartActivity("child", ActivityKind.Internal, default(ActivityContext), tags: new Dictionary<string, object?>
            {
                ["posthogDistinctId"] = "child-user",
                ["sessionId"] = "child-session",
            });
            Assert.Equal("child-user", child!.GetTagItem("posthogDistinctId"));
            Assert.Equal("child-session", child.GetTagItem("sessionId"));
            if (fail)
            {
                throw new InvalidOperationException("request failed");
            }
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Headers["X-POSTHOG-DISTINCT-ID"] = "browser-user";
        context.Request.Headers["X-POSTHOG-SESSION-ID"] = "browser-session";

        if (fail)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline(context));
        }
        else
        {
            await pipeline(context);
        }

        Assert.Null(PostHogContext.Current);
        Assert.Equal("server-user", request.GetTagItem("posthogDistinctId"));
        Assert.Equal("server-session", request.GetTagItem("sessionId"));
        using var outside = source.StartActivity("outside");
        Assert.Null(outside!.GetTagItem("posthogDistinctId"));
        Assert.Null(outside.GetTagItem("sessionId"));
    }

    [Fact]
    public async Task KeepsConcurrentRequestsAndTheirChildActivitiesSeparate()
    {
        using var provider = CreateServices();
        using var source = new ActivitySource(Guid.NewGuid().ToString());
        using var exporter = Listen(source);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var pipeline = CreatePipeline(provider, async context =>
        {
            if (Interlocked.Increment(ref entered) == 2)
            {
                ready.SetResult();
            }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var child = source.StartActivity("child");
            Assert.Equal(context.Request.Headers["X-POSTHOG-DISTINCT-ID"].ToString(), child!.GetTagItem("posthogDistinctId"));
            Assert.Equal(context.Request.Headers["X-POSTHOG-SESSION-ID"].ToString(), child.GetTagItem("sessionId"));
        });

        await Task.WhenAll(RunRequest("first"), RunRequest("second"));
        Assert.Null(PostHogContext.Current);

        async Task RunRequest(string id)
        {
            using var request = new Activity("request");
            request.Start();
            var context = new DefaultHttpContext();
            context.Request.Headers["X-POSTHOG-DISTINCT-ID"] = id;
            context.Request.Headers["X-POSTHOG-SESSION-ID"] = $"session-{id}";
            await pipeline(context);
            Assert.Equal(id, request.GetTagItem("posthogDistinctId"));
            Assert.Equal($"session-{id}", request.GetTagItem("sessionId"));
        }
    }

    [Theory]
    [InlineData(ActivitySamplingResult.None)]
    [InlineData(ActivitySamplingResult.PropagationData)]
    [InlineData(ActivitySamplingResult.AllDataAndRecorded)]
    public async Task LeavesApplicationSamplingAndListenerLifetimeUnchanged(ActivitySamplingResult sampling)
    {
        using var source = new ActivitySource(Guid.NewGuid().ToString());
        using var exporter = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => sampling,
        };
        ActivitySource.AddActivityListener(exporter);
        using (var provider = CreateServices())
        {
            var pipeline = CreatePipeline(provider, _ =>
            {
                using var child = source.StartActivity("child");
                Assert.Equal(sampling == ActivitySamplingResult.None, child is null);
                Assert.Equal(sampling == ActivitySamplingResult.AllDataAndRecorded, child?.Recorded ?? false);
                return Task.CompletedTask;
            });
            await pipeline(new DefaultHttpContext());
        }
        exporter.Dispose();
        Assert.False(source.HasListeners());
        Assert.Null(source.StartActivity("after-disposal"));
    }

    [Fact]
    public async Task DoesNotUseEndedRequestContextInBackgroundWork()
    {
        using var provider = CreateServices();
        using var source = new ActivitySource(Guid.NewGuid().ToString());
        using var exporter = Listen(source);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? background = null;
        var pipeline = CreatePipeline(provider, _ =>
        {
            background = Task.Run(async () =>
            {
                await ended.Task;
                using var child = source.StartActivity("background");
                Assert.Null(child!.GetTagItem("posthogDistinctId"));
                Assert.Null(child.GetTagItem("sessionId"));
            });
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Headers["X-POSTHOG-DISTINCT-ID"] = "browser-user";
        context.Request.Headers["X-POSTHOG-SESSION-ID"] = "browser-session";

        await pipeline(context);
        ended.SetResult();
        await background!;
    }

    [Fact]
    public async Task SupportsExistingMiddlewareSetupWithoutAspNetCoreServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IPostHogClient>());
        using var provider = services.BuildServiceProvider();
        using var request = new Activity("request");
        request.Start();
        var pipeline = CreatePipeline(provider, _ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-POSTHOG-SESSION-ID"] = "browser-session";

        await pipeline(context);

        Assert.Equal("browser-session", request.GetTagItem("sessionId"));
        Assert.Null(PostHogContext.Current);
    }

    static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        new PostHogConfigurationBuilder(services).UseAspNetCore();
        services.AddSingleton(Substitute.For<IPostHogClient>());
        return services.BuildServiceProvider();
    }

    static RequestDelegate CreatePipeline(
        IServiceProvider provider,
        RequestDelegate next,
        Action<PostHogRequestContextOptions>? configure = null)
    {
        var app = new ApplicationBuilder(provider);
        app.UsePostHogRequestContext(configure);
        app.Run(next);
        return app.Build();
    }

    static ActivityListener Listen(ActivitySource source, Action<Activity>? onStopped = null)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = onStopped,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
