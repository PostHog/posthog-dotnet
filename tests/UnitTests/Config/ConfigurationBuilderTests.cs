using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PostHog;
using PostHog.Config;
using PostHog.Library;

namespace ConfigurationBuilderTests;

public class TheConstructor
{
    [Fact]
    public void AddsSomeDefaultServices()
    {
        var services = new ServiceCollection();

        _ = new PostHogConfigurationBuilder(services);

        Assert.Contains(services, s => s.ServiceType == typeof(ILoggerFactory));
        Assert.Contains(services, s => s.ServiceType == typeof(IHttpClientFactory));
    }
}

public class TheUseMethod
{
    [Fact]
    public void RegistersServiceInMainServiceCollection()
    {
        var services = new ServiceCollection();
        var builder = new PostHogConfigurationBuilder(services);
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(Foo));

        builder.Use(s =>
        {
            s.AddSingleton<Foo>();
        });

        Assert.Contains(services, s => s.ServiceType == typeof(Foo));
    }

    public class Foo;
}

public class TheBuildMethod
{
    [Fact]
    public void AddsDefaultServicesRegistrations()
    {
        var services = new ServiceCollection();
        var builder = new PostHogConfigurationBuilder(services);

        builder.Build();

        Assert.Contains(services, s => s.ServiceType == typeof(IFeatureFlagCache));
        Assert.Contains(services, s => s.ServiceType == typeof(ITaskScheduler));
        Assert.Contains(services, s => s.ServiceType == typeof(IHttpClientFactory));
        Assert.Contains(services, s => s.ServiceType == typeof(IPostHogClient));
        Assert.Contains(services, s => s.ServiceType == typeof(TimeProvider));
        Assert.Contains(services, s => s.ServiceType == typeof(ILoggerFactory));

        // Make sure I can create an instance of PostHogClient.
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var client = provider.GetRequiredService<IPostHogClient>();
        Assert.NotNull(client);
        Assert.IsType<PostHogClient>(client);
    }
}
