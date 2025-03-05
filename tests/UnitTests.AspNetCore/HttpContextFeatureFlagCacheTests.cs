using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PostHog;
using PostHog.Cache;
using PostHog.Features;
using UnitTests.Fakes;

namespace HttpContextFeatureFlagCacheTests;

public class TheGetAndCacheFeatureFlagsAsyncMethod
{
    [Fact]
    public async Task CachesFlagsInHttpContext()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);
        var distinctId = "user123";
        var featureFlags = new Dictionary<string, FeatureFlag>
        {
            { "feature1", new FeatureFlag { Key = "feature1", IsEnabled = true } }
        };

        Func<CancellationToken, Task<IReadOnlyDictionary<string, FeatureFlag>>> fetcher = _ =>
            Task.FromResult((IReadOnlyDictionary<string, FeatureFlag>)featureFlags);

        var result = await cache.GetAndCacheFeatureFlagsAsync(distinctId, fetcher, CancellationToken.None);

        Assert.Equal(featureFlags, result);
        Assert.Equal(featureFlags, httpContext.Items[$"$PostHog(feature_flags):{distinctId}"]);
    }

    [Fact]
    public async Task ReturnsCachedFlagsFromHttpContext()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var distinctId = "user123";
        var cachedFeatureFlags = new Dictionary<string, FeatureFlag>
        {
            { "feature1", new FeatureFlag { Key = "feature1", IsEnabled = true } }
        };
        httpContext.Items[$"$PostHog(feature_flags):{distinctId}"] = cachedFeatureFlags;
        httpContextAccessor.HttpContext.Returns(httpContext);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);

        Func<CancellationToken, Task<IReadOnlyDictionary<string, FeatureFlag>>> fetcher = _ =>
            Task.FromResult((IReadOnlyDictionary<string, FeatureFlag>)new Dictionary<string, FeatureFlag>());

        var result = await cache.GetAndCacheFeatureFlagsAsync(distinctId, fetcher, CancellationToken.None);

        Assert.Equal(cachedFeatureFlags, result);
    }

    [Fact]
    public async Task DoesNotCacheIfHttpContextIsNull()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext)null!);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);
        var distinctId = "user123";
        var featureFlags = new Dictionary<string, FeatureFlag>
        {
            { "feature1", new FeatureFlag { Key = "feature1", IsEnabled = true } }
        };

        Func<CancellationToken, Task<IReadOnlyDictionary<string, FeatureFlag>>> fetcher = _ =>
            Task.FromResult((IReadOnlyDictionary<string, FeatureFlag>)featureFlags);

        var result = await cache.GetAndCacheFeatureFlagsAsync(distinctId, fetcher, CancellationToken.None);

        Assert.Equal(featureFlags, result);
    }

    [Fact]
    public async Task RetrievesFlagFromFetchEvenIfHttpContextItemsDisposedWhenGetting()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = Substitute.For<HttpContext>();
        var items = Substitute.For<IDictionary<object, object?>>();
        httpContext.Items.Returns(items);

        items[Arg.Any<string>()].Throws(new ObjectDisposedException("It disposed"));
        httpContextAccessor.HttpContext.Returns(httpContext);
        var container = new TestContainer(services =>
        {
            services.AddSingleton<IFeatureFlagCache>(new HttpContextFeatureFlagCache(httpContextAccessor));
        });
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
            {"featureFlags":{"flag-key": true, "another-flag-key": "some-value"}}
            """
        );
        var client = container.Activate<PostHogClient>();

        var flags = await client.GetAllFeatureFlagsAsync(distinctId: "1234");

        Assert.NotEmpty(flags);
        Assert.Equal("some-value", flags["another-flag-key"]);
    }

    [Fact]
    public async Task RetrievesFlagFromFetchEvenIfHttpContextItemsDisposedWhenSetting()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = Substitute.For<HttpContext>();
        var items = Substitute.For<IDictionary<object, object?>>();
        httpContext.Items.Returns(items);

        items[Arg.Any<string>()].Returns(null);
        items.When(x => x[Arg.Any<object>()] = Arg.Any<object?>())
            .Do(x => throw new ObjectDisposedException("It disposed"));
        httpContextAccessor.HttpContext.Returns(httpContext);
        var container = new TestContainer(services =>
        {
            services.AddSingleton<IFeatureFlagCache>(new HttpContextFeatureFlagCache(httpContextAccessor));
        });
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
            {"featureFlags":{"flag-key": true, "another-flag-key": "some-value"}}
            """
        );
        var client = container.Activate<PostHogClient>();

        var flags = await client.GetAllFeatureFlagsAsync(distinctId: "1234");

        Assert.NotEmpty(flags);
        Assert.Equal("some-value", flags["another-flag-key"]);
    }

    [Fact]
    public async Task RetrievesFlagFromHttpContextCacheOnSecondCall()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Returns(httpContext);
        var container = new TestContainer(services =>
        {
            services.AddSingleton<IFeatureFlagCache>(new HttpContextFeatureFlagCache(httpContextAccessor));
        });
        container.FakeHttpMessageHandler.AddDecideResponse(
            """
            {"featureFlags":{"flag-key": true, "another-flag-key": "some-value"}}
            """
        );
        var client = container.Activate<PostHogClient>();

        var flags = await client.GetAllFeatureFlagsAsync(distinctId: "1234");
        var flagsAgain = await client.GetAllFeatureFlagsAsync(distinctId: "1234");
        var firstFlag = await client.GetFeatureFlagAsync("flag-key", "1234");

        Assert.NotEmpty(flags);
        Assert.Same(flags, flagsAgain);
        Assert.NotNull(firstFlag);
        Assert.Equal("flag-key", firstFlag.Key);
    }
}