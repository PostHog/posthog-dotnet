using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PostHog;
using PostHog.Api;
using PostHog.Cache;
using PostHog.Features;
using UnitTests.Fakes;

namespace HttpContextFeatureFlagCacheTests;

public class TheGetAndCacheFlagsAsyncMethod
{
    [Fact]
    public async Task CachesFlagsInHttpContext()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);
        const string distinctId = "user123";
        var flagsResult = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "feature1", new FeatureFlag { Key = "feature1", IsEnabled = true } }
            },
            RequestId = "the-request-id"
        };

        var result = await cache.GetAndCacheFlagsAsync(
            distinctId,
            (distId, ctx) => Task.FromResult<FlagsResult>(flagsResult),
            CancellationToken.None);

        Assert.Equal(flagsResult, result);
        Assert.Equal(flagsResult, httpContext.Items[$"$PostHog(feature_flags):{distinctId}"]);
    }

    [Fact]
    public async Task ReturnsCachedFlagsFromHttpContext()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var distinctId = "user123";
        var cachedFlagsResult =
        new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "feature1", new FeatureFlag { Key = "feature1", IsEnabled = true } }
            },
            RequestId = "a-request-id",
            ErrorsWhileComputingFlags = true
        };

        httpContext.Items[$"$PostHog(feature_flags):{distinctId}"] = cachedFlagsResult;
        httpContextAccessor.HttpContext.Returns(httpContext);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);

        var result = await cache.GetAndCacheFlagsAsync(
            distinctId,
            (_, _) => Task.FromResult(new FlagsResult()),
            CancellationToken.None);

        Assert.Equal(cachedFlagsResult, result);
    }

    [Fact]
    public async Task DoesNotCacheIfHttpContextIsNull()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext)null!);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);
        var distinctId = "user123";
        var flagsResult = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "feature1", new FeatureFlag { Key = "feature1", IsEnabled = true } }
            }
        };

        var result = await cache.GetAndCacheFlagsAsync(
            distinctId,
            (_, _) => Task.FromResult(flagsResult),
            CancellationToken.None);

        Assert.Equal(flagsResult, result);
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
        container.FakeHttpMessageHandler.AddFlagsResponse(
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
            .Do(_ => throw new ObjectDisposedException("It disposed"));
        httpContextAccessor.HttpContext.Returns(httpContext);
        var container = new TestContainer(services =>
        {
            services.AddSingleton<IFeatureFlagCache>(new HttpContextFeatureFlagCache(httpContextAccessor));
        });
        container.FakeHttpMessageHandler.AddFlagsResponse(
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
        container.FakeHttpMessageHandler.AddFlagsResponse(
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

    [Fact]
    public async Task FetchesNewFlagsWhenPersonPropertiesDiffer()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);
        var distinctId = "user123";
        var personProperties1 = new Dictionary<string, object?> { ["email"] = "test1@example.com" };
        var personProperties2 = new Dictionary<string, object?> { ["email"] = "test2@example.com" };

        var flags1 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
            }
        };

        var flags2 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = false } }
            }
        };

        var fetchCount = 0;
        Func<string, CancellationToken, Task<FlagsResult>> fetcher = (_, _) =>
        {
            fetchCount++;
            return Task.FromResult(fetchCount == 1 ? flags1 : flags2);
        };

        var result1 = await cache.GetAndCacheFlagsAsync(distinctId, personProperties1, null, fetcher, CancellationToken.None);
        var result2 = await cache.GetAndCacheFlagsAsync(distinctId, personProperties2, null, fetcher, CancellationToken.None);

        Assert.Equal(2, fetchCount); // Should fetch twice because properties differ
        Assert.NotSame(result1, result2);
        Assert.True(result1.Flags["flag1"].IsEnabled);
        Assert.False(result2.Flags["flag1"].IsEnabled);
    }

    [Fact]
    public async Task ReturnsCachedFlagsWhenPersonPropertiesMatch()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);
        var distinctId = "user123";
        var personProperties = new Dictionary<string, object?> { ["email"] = "test@example.com" };

        var flags = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
            }
        };

        var fetchCount = 0;
        Func<string, CancellationToken, Task<FlagsResult>> fetcher = (_, _) =>
        {
            fetchCount++;
            return Task.FromResult(flags);
        };

        var result1 = await cache.GetAndCacheFlagsAsync(distinctId, personProperties, null, fetcher, CancellationToken.None);
        var result2 = await cache.GetAndCacheFlagsAsync(distinctId, personProperties, null, fetcher, CancellationToken.None);

        Assert.Equal(1, fetchCount); // Should only fetch once
        Assert.Same(result1, result2);
    }

    [Fact]
    public async Task FetchesNewFlagsWhenGroupsDiffer()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);
        var distinctId = "user123";
        var groups1 = new GroupCollection { { "company", "acme" } };
        var groups2 = new GroupCollection { { "company", "initech" } };

        var flags1 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
            }
        };

        var flags2 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = false } }
            }
        };

        var fetchCount = 0;
        Func<string, CancellationToken, Task<FlagsResult>> fetcher = (_, _) =>
        {
            fetchCount++;
            return Task.FromResult(fetchCount == 1 ? flags1 : flags2);
        };

        var result1 = await cache.GetAndCacheFlagsAsync(distinctId, null, groups1, fetcher, CancellationToken.None);
        var result2 = await cache.GetAndCacheFlagsAsync(distinctId, null, groups2, fetcher, CancellationToken.None);

        Assert.Equal(2, fetchCount); // Should fetch twice because groups differ
        Assert.NotSame(result1, result2);
        Assert.True(result1.Flags["flag1"].IsEnabled);
        Assert.False(result2.Flags["flag1"].IsEnabled);
    }

    [Fact]
    public async Task FetchesNewFlagsWhenGroupPropertiesDiffer()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);
        var distinctId = "user123";
        var groups1 = new GroupCollection
        {
            new Group("company", "acme") { ["tier"] = "enterprise" }
        };
        var groups2 = new GroupCollection
        {
            new Group("company", "acme") { ["tier"] = "starter" }
        };

        var flags1 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
            }
        };

        var flags2 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = false } }
            }
        };

        var fetchCount = 0;
        Func<string, CancellationToken, Task<FlagsResult>> fetcher = (_, _) =>
        {
            fetchCount++;
            return Task.FromResult(fetchCount == 1 ? flags1 : flags2);
        };

        var result1 = await cache.GetAndCacheFlagsAsync(distinctId, null, groups1, fetcher, CancellationToken.None);
        var result2 = await cache.GetAndCacheFlagsAsync(distinctId, null, groups2, fetcher, CancellationToken.None);

        Assert.Equal(2, fetchCount); // Should fetch twice because group properties differ
        Assert.NotSame(result1, result2);
        Assert.True(result1.Flags["flag1"].IsEnabled);
        Assert.False(result2.Flags["flag1"].IsEnabled);
    }

    [Fact]
    public async Task FetchesNewFlagsWhenNullVsNonNullProperties()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);
        var distinctId = "user123";
        var personProperties = new Dictionary<string, object?> { ["email"] = "test@example.com" };

        var flags1 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = true } }
            }
        };

        var flags2 = new FlagsResult
        {
            Flags = new Dictionary<string, FeatureFlag>
            {
                { "flag1", new FeatureFlag { Key = "flag1", IsEnabled = false } }
            }
        };

        var fetchCount = 0;
        Func<string, CancellationToken, Task<FlagsResult>> fetcher = (_, _) =>
        {
            fetchCount++;
            return Task.FromResult(fetchCount == 1 ? flags1 : flags2);
        };

        var result1 = await cache.GetAndCacheFlagsAsync(distinctId, null, null, fetcher, CancellationToken.None);
        var result2 = await cache.GetAndCacheFlagsAsync(distinctId, personProperties, null, fetcher, CancellationToken.None);

        Assert.Equal(2, fetchCount); // Should fetch twice because one has properties and one doesn't
        Assert.NotSame(result1, result2);
        Assert.True(result1.Flags["flag1"].IsEnabled);
        Assert.False(result2.Flags["flag1"].IsEnabled);
    }
}

// Legacy tests
public class TheGetAndCacheFeatureFlagsAsyncMethod
{
    [Fact]
    public async Task CachesFlagsInHttpContext()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);
        const string distinctId = "user123";
        var featureFlags = new Dictionary<string, FeatureFlag>
        {
            { "feature1", new FeatureFlag { Key = "feature1", IsEnabled = true } }
        };

        var result = await cache.GetAndCacheFeatureFlagsAsync(
            distinctId,
            _ => Task.FromResult<IReadOnlyDictionary<string, FeatureFlag>>(featureFlags),
            CancellationToken.None);

        Assert.Equal(featureFlags, result);
        Assert.Equal(
            new FlagsResult { Flags = featureFlags },
            httpContext.Items[$"$PostHog(feature_flags):{distinctId}"]);
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
        httpContext.Items[$"$PostHog(feature_flags):{distinctId}"] = new FlagsResult { Flags = cachedFeatureFlags };
        httpContextAccessor.HttpContext.Returns(httpContext);

        var cache = new HttpContextFeatureFlagCache(httpContextAccessor);

        var result = await cache.GetAndCacheFeatureFlagsAsync(
            distinctId,
            _ => Task.FromResult((IReadOnlyDictionary<string, FeatureFlag>)new Dictionary<string, FeatureFlag>()),
            CancellationToken.None);

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

        var result = await cache.GetAndCacheFeatureFlagsAsync(
            distinctId,
            _ => Task.FromResult((IReadOnlyDictionary<string, FeatureFlag>)featureFlags),
            CancellationToken.None);

        Assert.Equal(featureFlags, result);
    }
}