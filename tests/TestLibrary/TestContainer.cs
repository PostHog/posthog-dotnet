using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PostHog;
using PostHog.Config;
using PostHog.Library;
using TestLibrary.Fakes;
using Xunit.Abstractions;

namespace UnitTests.Fakes;

#pragma warning disable CA2000

/// <summary>
/// A container for dependency injection in unit tests. Provides some commonly used fakes.
/// </summary>
public sealed class TestContainer : IServiceProvider
{
    readonly ServiceProvider _serviceProvider;

    /// <summary>
    /// Creates a <see cref="TestContainer"/> instance with the registered services.
    /// </summary>
    /// <param name="configureServices"></param>
    public TestContainer(Action<IServiceCollection>? configureServices = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        ConfigureServices(services);
        configureServices?.Invoke(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    // Convenience constructor.
    public TestContainer(string personalApiKey) : this(services =>
    {
        services.Configure<PostHogOptions>(options =>
        {
            options.ProjectApiKey = "fake-project-api-key";
            options.PersonalApiKey = personalApiKey;
        });
    })
    {
    }

    // Convenience constructor.
    public TestContainer(bool disableGeoIp) : this(services =>
    {
        services.Configure<PostHogOptions>(options =>
        {
            options.SuperProperties.Add("$geoip_disable", disableGeoIp);
        });
    })
    {
    }

    public FakeHttpMessageHandler FakeHttpMessageHandler { get; } = new();

    public FakeTimeProvider FakeTimeProvider { get; } = new();

    public FakeTaskScheduler FakeTaskScheduler { get; } = new();

    public FakeLoggerProvider FakeLoggerProvider { get; } = new() { TestOutputHelper = new ConsoleTestOutputHelper() };

    public ConsoleTestOutputHelper ConsoleTestOutputHelper => FakeLoggerProvider.TestOutputHelper as ConsoleTestOutputHelper ?? new ConsoleTestOutputHelper();

    void ConfigureServices(IServiceCollection services)
    {
        services.Configure<PostHogOptions>(options =>
        {
            options.ProjectApiKey = "fake-project-api-key";
        });
        services.AddSingleton<ITestOutputHelper>(ConsoleTestOutputHelper);
        services.AddSingleton<FakeLoggerProvider>();
        services.AddSingleton<ILoggerFactory>(FakeLoggerProvider);
        services.AddSingleton<ILoggerProvider>(FakeLoggerProvider);
        services.AddSingleton<HttpMessageHandler>(FakeHttpMessageHandler);
        services.AddSingleton<TimeProvider>(_ =>
        {
            FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
            return FakeTimeProvider;
        });
        services.AddSingleton<ITaskScheduler>(FakeTaskScheduler);
        services.AddHttpClient(nameof(PostHogClient))
            .ConfigurePrimaryHttpMessageHandler(() => FakeHttpMessageHandler);
        services.AddSingleton<IFeatureFlagCache>(NullFeatureFlagCache.Instance);
        services.AddSingleton<IPostHogClient, PostHogClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var cache = sp.GetRequiredService<IFeatureFlagCache>();
            var options = sp.GetRequiredService<IOptions<PostHogOptions>>();
            var taskScheduler = sp.GetRequiredService<ITaskScheduler>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new PostHogClient(options, cache, httpClientFactory, taskScheduler, timeProvider, loggerFactory);
        });
    }

    /// <summary>
    /// Activates a new instance of <typeparamref name="T" /> using the services registered in the container.
    /// Use this for unit test subjects.
    /// </summary>
    /// <typeparam name="T">The type to activate.</typeparam>
    public T Activate<T>(params object[] parameters) where T : class
        => ActivatorUtilities.CreateInstance<T>(_serviceProvider, parameters);

    object? IServiceProvider.GetService(Type serviceType) => _serviceProvider.GetService(serviceType);
}