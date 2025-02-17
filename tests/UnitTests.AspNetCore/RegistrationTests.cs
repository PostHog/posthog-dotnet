using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PostHog;
using PostHog.Config;

namespace RegistrationTests;

public class TheAddPostHogMethod
{
    [Fact]
    public void ReadsSettingsFromPostHogConfigurationSection()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Host.UseDefaultServiceProvider((_, options) =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        var services = builder.Services;
        var configuration = builder.Configuration;
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PostHog:PersonalApiKey"] = "fake-secret",
            ["PostHog:ProjectApiKey"] = "fake-not-so-secret",
            ["PostHog:HostUrl"] = "https://test-host.com",
            ["PostHog:FeatureFlagPollInterval"] = "00:00:10",
        });

        builder.AddPostHog();

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IPostHogClient>());
        var options = provider.GetRequiredService<IOptions<PostHogOptions>>().Value;
        Assert.Equal("fake-secret", options.PersonalApiKey);
        Assert.Equal("fake-not-so-secret", options.ProjectApiKey);
        Assert.Equal(new Uri("https://test-host.com"), options.HostUrl);
        Assert.Equal(TimeSpan.FromSeconds(10), options.FeatureFlagPollInterval);
    }


    [Fact]
    async Task CanConfigureServices()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Host.UseDefaultServiceProvider((_, options) =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        var services = builder.Services;
        var configuration = builder.Configuration;
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PostHogLocal:PersonalApiKey"] = "fake-secret",
            ["PostHogLocal:ProjectApiKey"] = "fake-not-so-secret",
            ["PostHogLocal:HostUrl"] = "https://local-test-host.com",
            ["PostHogLocal:FeatureFlagPollInterval"] = "00:00:20",
        });
        using var fakeDelegatingHandler = new FakeDelegatingHandler();
        services.AddSingleton(fakeDelegatingHandler);

        builder.AddPostHog(options =>
        {
            // In general this call is not needed. The default settings are in the "PostHoc" configuration section.
            // This is here so I can easily switch testing against my local install and production.
            options.UseConfigurationSection(builder.Configuration.GetSection("PostHogLocal"));
            // Logs requests and responses. Fine for a sample project. Probably not good for production.
            options.ConfigureHttpClient(httpClientBuilder => httpClientBuilder.AddHttpMessageHandler<FakeDelegatingHandler>());
        });

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IPostHogClient>());
        var options = provider.GetRequiredService<IOptions<PostHogOptions>>().Value;
        Assert.Equal("fake-secret", options.PersonalApiKey);
        Assert.Equal("fake-not-so-secret", options.ProjectApiKey);
        Assert.Equal(new Uri("https://local-test-host.com"), options.HostUrl);
        Assert.Equal(TimeSpan.FromSeconds(20), options.FeatureFlagPollInterval);
        // Confirm the HttpClient has the message handler.
        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient(nameof(PostHogClient));
        Assert.Null(fakeDelegatingHandler.SentRequest);
        await httpClient.GetAsync(new Uri("https://example.com"));
        Assert.NotNull(fakeDelegatingHandler.SentRequest);
        Assert.Equal(HttpMethod.Get, fakeDelegatingHandler.SentRequest.Method);
        Assert.Equal(new Uri("https://example.com"), fakeDelegatingHandler.SentRequest.RequestUri);
    }
}

public class FakeDelegatingHandler : DelegatingHandler
{
    public HttpRequestMessage? SentRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SentRequest = request;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
