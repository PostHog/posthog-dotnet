using PostHog;
using UnitTests.Fakes;

namespace RemoteConfigTests;

public class TheGetRemoteConfigPayloadAsyncMethod
{
    [Fact]
    public async Task ReturnsNullForNonExistentKey()
    {
        var container = new TestContainer("fake-personal-api-key");
        var client = container.Activate<PostHogClient>();

        var result = await client.GetRemoteConfigPayloadAsync("non-existent-key");

        Assert.Null(result);
    }

    [Fact]
    public async Task ReturnsJsonPayloadForKey()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddRemoteConfigResponse(
            "remote-config-key",
            """
            {"foo": "bar","baz": 42}
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetRemoteConfigPayloadAsync("remote-config-key");

        Assert.NotNull(result);
        Assert.Equal("bar", result.RootElement.GetProperty("foo").GetString());
        JsonAssert.Equal("""{"foo": "bar","baz": 42}""", result);
    }

    [Fact]
    public async Task HandlesJsonEncodedInString()
    {
        var container = new TestContainer("fake-personal-api-key");
        // Right now, the endpoint doesn't return JSON. It returns a string that contains JSON.
        container.FakeHttpMessageHandler.AddRemoteConfigResponse(
            "remote-config-key",
            """
            "{\"an encrypted\": \"payload\"}"
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetRemoteConfigPayloadAsync("remote-config-key");

        Assert.NotNull(result);
        Assert.Equal("payload", result.RootElement.GetProperty("an encrypted").GetString());
    }

    [Fact]
    public async Task HandlesJsonStringPayload()
    {
        var container = new TestContainer("fake-personal-api-key");
        // Right now, the endpoint doesn't return JSON. It returns a string that contains JSON.
        container.FakeHttpMessageHandler.AddRemoteConfigResponse(
            "remote-config-key",
            """
            "Valid JSON string"
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetRemoteConfigPayloadAsync("remote-config-key");

        Assert.NotNull(result);
        Assert.Equal("Valid JSON string", result.RootElement.GetString());
    }

    [Fact]
    public async Task HandlesJsonEncodedStringPayload()
    {
        var container = new TestContainer("fake-personal-api-key");
        // Right now, the endpoint doesn't return JSON. It returns a string that contains JSON.
        container.FakeHttpMessageHandler.AddRemoteConfigResponse(
            "remote-config-key",
            """
            "\"Valid JSON string\""
            """
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetRemoteConfigPayloadAsync("remote-config-key");

        Assert.NotNull(result);
        Assert.Equal("Valid JSON string", result.RootElement.GetString());
    }

    [Fact]
    public async Task IncludesProjectApiKeyTokenInRemoteConfigUrl()
    {
        var container = new TestContainer("fake-personal-api-key");

        // Use the AddResponse method to verify the URL includes the token parameter
        container.FakeHttpMessageHandler.AddResponse(
            new Uri("https://us.i.posthog.com/api/projects/@current/feature_flags/test-flag/remote_config?token=fake-project-api-key"),
            HttpMethod.Get,
            responseBody: """{"test": "payload"}"""
        );
        var client = container.Activate<PostHogClient>();

        var result = await client.GetRemoteConfigPayloadAsync("test-flag");

        Assert.NotNull(result);
        Assert.Equal("payload", result.RootElement.GetProperty("test").GetString());
    }
}