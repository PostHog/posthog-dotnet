using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostHog;
using PostHog.Cache;
using PostHog.Config;

namespace PostHogConfigurationBuilderExtensionsTests;

public class TheUseAspNetCoreMethod
{
    [Fact]
    public void BuildsPostHogClientWithAspNetCoreServices()
    {
        var services = new ServiceCollection();
        var builder = new PostHogConfigurationBuilder(services);

        builder.UseAspNetCore();

        // Make sure I can create an instance of PostHogClient.
        var provider = services.BuildServiceProvider();

        Assert.IsType<HttpContextFeatureFlagCache>(provider.GetRequiredService<IFeatureFlagCache>());
        Assert.Single(services, s => s.ServiceType == typeof(IHttpContextAccessor));
        Assert.NotNull(provider.GetRequiredService<IOptions<PostHogOptions>>());
    }
}