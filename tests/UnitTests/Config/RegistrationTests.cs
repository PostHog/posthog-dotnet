using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog;
using PostHog.Config;
using PostHog.Library;

namespace RegistrationTests;

public class TheAddPostHogMethod
{
    [Fact]
    public void RegistersServicesRequiredByPostHogClient()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddPostHog();

        Assert.Contains(services, s => s.ServiceType == typeof(IPostHogClient));
        Assert.Contains(services, s => s.ServiceType == typeof(IFeatureFlagCache));
        Assert.Contains(services, s => s.ServiceType == typeof(ITaskScheduler));
        Assert.Contains(services, s => s.ServiceType == typeof(IHttpClientFactory));
        Assert.Contains(services, s => s.ServiceType == typeof(TimeProvider));
        Assert.Contains(services, s => s.ServiceType == typeof(ILoggerFactory));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        // Make sure I can create an instance of PostHogClient.
        var client = provider.GetRequiredService<IPostHogClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void CanReadConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PostHogTest:PersonalApiKey"] = "fake-secret-personal-api-key",
                ["PostHogTest:ProjectApiKey"] = "fake-public-project-api-key",
                ["PostHogTest:HostUrl"] = "https://test-host/",
                ["PostHogTest:FeatureFlagPollInterval"] = "00:00:10",
                ["PostHogTest:FlushAt"] = "10",
                ["PostHogTest:MaxBatchSize"] = "99",
                ["PostHogTest:SuperProperties:Castle"] = "Winterfell",
                ["PostHogTest:SuperProperties:Family"] = "Starks"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddPostHog(
            options =>
            {
                options.UseConfigurationSection(configuration.GetSection("PostHogTest"));
            });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var options = provider.GetRequiredService<IOptions<PostHogOptions>>().Value;

        Assert.Equal("fake-public-project-api-key", options.ProjectApiKey);
        Assert.Equal("fake-secret-personal-api-key", options.PersonalApiKey);
        Assert.Equal(new Uri("https://test-host/"), options.HostUrl);
        Assert.Equal(TimeSpan.FromSeconds(10), options.FeatureFlagPollInterval);
        Assert.Equal(10, options.FlushAt);
        Assert.Equal(99, options.MaxBatchSize);
        Assert.Equal(new()
        {
            ["Castle"] = "Winterfell",
            ["Family"] = "Starks"
        }, options.SuperProperties);
        // Check the defaults
        Assert.Equal(1000, options.MaxQueueSize);
        Assert.Equal(TimeSpan.FromSeconds(30), options.FlushInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), options.FeatureFlagSentCacheSlidingExpiration);
        Assert.Equal(50_000, options.FeatureFlagSentCacheSizeLimit);
    }

    [Fact]
    public void AllowsOverridingConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PostHogTest:PersonalApiKey"] = "fake-secret-personal-api-key",
                ["PostHogTest:ProjectApiKey"] = "fake-public-project-api-key",
                ["PostHogTest:HostUrl"] = "https://test-host/",
                ["PostHogTest:FeatureFlagPollInterval"] = "00:00:10",
                ["PostHogTest:FlushAt"] = "10",
                ["PostHogTest:MaxBatchSize"] = "99",
                ["PostHogTest:SuperProperties:Castle"] = "Winterfell",
                ["PostHogTest:SuperProperties:Family"] = "Starks"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddPostHog(
            options =>
            {
                options.UseConfigurationSection(configuration.GetSection("PostHogTest"));
                options.PostConfigure(o =>
                {
                    o.FlushAt = 42;
                    o.MaxBatchSize = 123;
                    o.MaxQueueSize = 4200;
                    o.SuperProperties["Castle"] = "Black";
                    o.SuperProperties.Add("House", "Tully");
                });
            });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var options = provider.GetRequiredService<IOptions<PostHogOptions>>().Value;

        Assert.Equal("fake-public-project-api-key", options.ProjectApiKey);
        Assert.Equal("fake-secret-personal-api-key", options.PersonalApiKey);
        Assert.Equal(new Uri("https://test-host/"), options.HostUrl);
        Assert.Equal(TimeSpan.FromSeconds(10), options.FeatureFlagPollInterval);
        Assert.Equal(42, options.FlushAt);
        Assert.Equal(123, options.MaxBatchSize);
        Assert.Equal(4200, options.MaxQueueSize);
        Assert.Equal(new()
        {
            ["Castle"] = "Black",
            ["Family"] = "Starks",
            ["House"] = "Tully"
        }, options.SuperProperties);
        // Check the defaults
        Assert.Equal(TimeSpan.FromSeconds(30), options.FlushInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), options.FeatureFlagSentCacheSlidingExpiration);
        Assert.Equal(50_000, options.FeatureFlagSentCacheSizeLimit);
    }
}