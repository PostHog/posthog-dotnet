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
        JsonAssert.Equal("""{"foo": "bar","baz": 42}""", result);
    }
}