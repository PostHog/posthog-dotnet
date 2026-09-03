using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            ["PostHog:ProjectToken"] = "fake-not-so-secret",
            ["PostHog:HostUrl"] = "https://test-host.com",
            ["PostHog:FeatureFlagPollInterval"] = "00:00:10",
        });

        builder.AddPostHog();

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IPostHogClient>());
        var options = provider.GetRequiredService<IOptions<PostHogOptions>>().Value;
        Assert.Equal("fake-secret", options.SecretKey);
        Assert.Equal("fake-not-so-secret", options.ProjectToken);
        Assert.Equal(new Uri("https://test-host.com"), options.HostUrl);
        Assert.Equal(TimeSpan.FromSeconds(10), options.FeatureFlagPollInterval);
    }

    [Fact]
    public async Task UsesAspNetCoreLibraryMetadata()
    {
        var builder = WebApplication.CreateSlimBuilder();
        using var messageHandler = new FakeHttpMessageHandler();
        var flagsRequestHandler = messageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        var batchRequestHandler = messageHandler.AddBatchResponse();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PostHog:ProjectToken"] = "fake-not-so-secret",
            ["PostHog:EnableCompression"] = "false",
        });
        builder.AddPostHog(options =>
            options.ConfigureHttpClient(httpClient =>
                httpClient.ConfigurePrimaryHttpMessageHandler(() => messageHandler)));

        await using var provider = builder.Services.BuildServiceProvider();
        var client = provider.GetRequiredService<IPostHogClient>();
        await client.GetAllFeatureFlagsAsync("test-user", null, CancellationToken.None);
        client.Capture("test-user", "test-event");
        await client.FlushAsync();

        var expectedVersion = typeof(Registration).Assembly.GetName().Version?.ToString(3);
        using var flagsDocument = JsonDocument.Parse(flagsRequestHandler.GetReceivedRequestBody(indented: false));
        var flagsProperties = flagsDocument.RootElement.GetProperty("properties");
        Assert.Equal("posthog-aspnetcore", flagsProperties.GetProperty("$lib").GetString());
        Assert.Equal(expectedVersion, flagsProperties.GetProperty("$lib_version").GetString());

        var userAgent = flagsRequestHandler.ReceivedRequest.Headers.UserAgent;
        var userAgentProduct = Assert.Single(userAgent, value => value.Product is not null).Product;
        Assert.Equal("posthog-aspnetcore", userAgentProduct?.Name);
        Assert.Equal(expectedVersion, userAgentProduct?.Version);

        using var batchDocument = JsonDocument.Parse(batchRequestHandler.GetReceivedRequestBody(indented: false));
        var eventProperties = batchDocument.RootElement.GetProperty("batch")[0].GetProperty("properties");
        Assert.Equal("posthog-aspnetcore", eventProperties.GetProperty("$lib").GetString());
        Assert.Equal(expectedVersion, eventProperties.GetProperty("$lib_version").GetString());
    }

    [Fact]
    public void DoesNotLogLegacyWarningWhenOnlyProjectTokenIsConfigured()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PostHog:ProjectToken"] = "fake-not-so-secret",
        });
        using var logger = new FakeLoggerProvider();
        builder.Services.AddSingleton<ILoggerFactory>(logger);

        builder.AddPostHog();

        using var provider = builder.Services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IPostHogClient>());
        Assert.DoesNotContain(
            logger.GetAllEvents(minimumLevel: LogLevel.Warning),
            log => log.Message?.Contains("ProjectApiKey is deprecated", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ReadsSecretKeyFromPostHogConfigurationSection()
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
            ["PostHog:SecretKey"] = "fake-secret",
            ["PostHog:ProjectToken"] = "fake-not-so-secret",
        });

        builder.AddPostHog();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PostHogOptions>>().Value;
        Assert.Equal("fake-secret", options.SecretKey);
        Assert.Equal("fake-not-so-secret", options.ProjectToken);
    }

    [Fact]
    public void ReadsLegacyProjectApiKeyFromPostHogConfigurationSection()
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
            ["PostHog:ProjectApiKey"] = "fake-not-so-secret",
        });
        using var logger = new FakeLoggerProvider();
        services.AddSingleton<ILoggerFactory>(logger);

        builder.AddPostHog();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IPostHogClient>());
        var options = provider.GetRequiredService<IOptions<PostHogOptions>>().Value;
        Assert.Equal("fake-not-so-secret", options.ProjectToken);
        Assert.Contains(
            logger.GetAllEvents(minimumLevel: LogLevel.Warning),
            log => log.Message?.Contains("ProjectApiKey is deprecated", StringComparison.Ordinal) == true);
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
            ["PostHogLocal:ProjectToken"] = "fake-not-so-secret",
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
        Assert.Equal("fake-secret", options.SecretKey);
        Assert.Equal("fake-not-so-secret", options.ProjectToken);
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
