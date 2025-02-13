using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog;
using PostHog.Config;
using PostHog.Versioning;
using UnitTests.Fakes;

#pragma warning disable CA2000
namespace PostHogClientTests;


public class TheIdentifyPersonAsyncMethod
{
    [Fact] // Similar to PostHog/posthog-python test_basic_identify
    public async Task SendsCorrectPayload()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddCaptureResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.IdentifyAsync("some-distinct-id");

        Assert.Equal(1, result.Status);
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "event": "$identify",
                         "distinct_id": "some-distinct-id",
                         "properties": {
                           "$lib": "posthog-dotnet",
                           "$lib_version": "{{VersionConstants.Version}}",
                           "$os": "{{RuntimeInformation.OSDescription}}",
                           "$framework": "{{RuntimeInformation.FrameworkDescription}}",
                           "$arch": "{{RuntimeInformation.ProcessArchitecture}}",
                           "$geoip_disable": true
                         },
                         "api_key": "fake-project-api-key",
                         "timestamp": "2024-01-21T19:08:23\u002B00:00"
                       }
                       """, received);
    }

    [Fact]
    public async Task SendsCorrectPayloadWithPersonProperties()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddCaptureResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.IdentifyAsync(
            distinctId: "some-distinct-id",
            email: "wildling-lover@example.com",
            name: "Jon Snow",
            personPropertiesToSet: new() { ["age"] = 36 },
            personPropertiesToSetOnce: new() { ["join_date"] = "2024-01-21" },
            CancellationToken.None);

        Assert.Equal(1, result.Status);
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "event": "$identify",
                         "distinct_id": "some-distinct-id",
                         "properties": {
                           "$set": {
                             "age": 36,
                             "email": "wildling-lover@example.com",
                             "name": "Jon Snow"
                           },
                           "$set_once": {
                             "join_date": "2024-01-21"
                           },
                           "$lib": "posthog-dotnet",
                           "$lib_version": "{{VersionConstants.Version}}",
                           "$os": "{{RuntimeInformation.OSDescription}}",
                           "$framework": "{{RuntimeInformation.FrameworkDescription}}",
                           "$arch": "{{RuntimeInformation.ProcessArchitecture}}",
                           "$geoip_disable": true
                         },
                         "api_key": "fake-project-api-key",
                         "timestamp": "2024-01-21T19:08:23\u002B00:00"
                       }
                       """, received);
    }

    [Fact] // Ported from PostHog/posthog-python test_basic_super_properties
    public async Task SendsCorrectPayloadWithSuperProperties()
    {
        var container = new TestContainer(sp =>
        {
            sp.AddSingleton<IOptions<PostHogOptions>>(new PostHogOptions
            {
                ProjectApiKey = "fake-project-api-key",
                SuperProperties = new Dictionary<string, object> { ["source"] = "repo-name" }
            });
        });
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddCaptureResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.IdentifyAsync("some-distinct-id");

        Assert.Equal(1, result.Status);
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "event": "$identify",
                         "distinct_id": "some-distinct-id",
                         "properties": {
                           "$lib": "posthog-dotnet",
                           "$lib_version": "{{VersionConstants.Version}}",
                           "$os": "{{RuntimeInformation.OSDescription}}",
                           "$framework": "{{RuntimeInformation.FrameworkDescription}}",
                           "$arch": "{{RuntimeInformation.ProcessArchitecture}}",
                           "$geoip_disable": true,
                           "source": "repo-name"
                         },
                         "api_key": "fake-project-api-key",
                         "timestamp": "2024-01-21T19:08:23\u002B00:00"
                       }
                       """, received);
    }
}

public class TheIdentifyGroupAsyncMethod
{
    [Fact] // Ported from PostHog/posthog-python test_basic_group_identify
    public async Task SendsCorrectPayload()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddCaptureResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.GroupIdentifyAsync(type: "organization", key: "id:5", "PostHog");

        Assert.Equal(1, result.Status);
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "event": "$groupidentify",
                         "distinct_id": "$organization_id:5",
                         "properties": {
                           "$group_type": "organization",
                           "$group_key": "id:5",
                           "$group_set": {
                             "name": "PostHog"
                           },
                           "$lib": "posthog-dotnet",
                           "$lib_version": "{{VersionConstants.Version}}",
                           "$os": "{{RuntimeInformation.OSDescription}}",
                           "$framework": "{{RuntimeInformation.FrameworkDescription}}",
                           "$arch": "{{RuntimeInformation.ProcessArchitecture}}",
                           "$geoip_disable": true
                         },
                         "api_key": "fake-project-api-key",
                         "timestamp": "2024-01-21T19:08:23\u002B00:00"
                       }
                       """, received);
    }
}

public class TheGetDecryptedFeatureFlagPayloadAsyncMethod
{
    [Fact]
    public async Task RetrievesDecryptedPayload()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddDecryptedPayloadResponse(
            key: "remote-config-key",
            responseBody: """{"foo" : "bar", "baz" : 42}"""
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetDecryptedFeatureFlagPayloadAsync("remote-config-key", CancellationToken.None);

        Assert.NotNull(result);
        JsonAssert.AreEqual("""{"foo":"bar","baz":42}""", result);
    }

    [Fact]
    public async Task ReturnsNullWhenNotAuthorizedAndLogsError()
    {
        var container = new TestContainer("personal-api-key");
        container.FakeHttpMessageHandler.AddResponse(
            new Uri("https://us.i.posthog.com/api/projects/@current/feature_flags/some-key/remote_config/"),
            HttpMethod.Get,
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    """
                    {
                        "type": "authentication_error",
                        "code": "authentication_failed",
                        "detail": "Incorrect authentication credentials.",
                        "attr": null
                    }
                    """
                )
            }
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetDecryptedFeatureFlagPayloadAsync("some-key", CancellationToken.None);

        Assert.Null(result);
        var logEvent = Assert.Single(container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Error));
        Assert.Equal(LogLevel.Error, logEvent.LogLevel);
        Assert.Equal("[FEATURE FLAGS] Error while fetching decrypted feature flag payload.", logEvent.Message);
        Assert.Equal("Incorrect authentication credentials.", logEvent.Exception?.Message);
    }
}