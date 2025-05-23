using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PostHog;
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

public class TheCaptureMethod
{
    [Fact]
    public async Task SendsEnrichedCapturedEventsWhenSendFeatureFlagsTrueButDoesNotMakeSameDecideCallTwice()
    {
        var container = new TestContainer();
        container.FakeHttpMessageHandler.AddCaptureResponse();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        // Only need three responses to cover the three events
        container.FakeHttpMessageHandler.AddRepeatedDecideResponse(3, i =>
            $$"""
            {"featureFlags": {"flag1":true, "flag2":false, "flag3":"variant-{{i}}"} }
            """
        );
        var client = container.Activate<PostHogClient>();

        client.Capture("some-distinct-id", "some-event", sendFeatureFlags: true);
        client.Capture("some-distinct-id", "some-event", sendFeatureFlags: true);
        client.Capture("another-distinct-id", "some-event", sendFeatureFlags: true);
        client.Capture("some-distinct-id", "some-event", sendFeatureFlags: true);
        client.Capture("some-distinct-id", "some-event", sendFeatureFlags: true);
        client.Capture("another-distinct-id", "some-event", sendFeatureFlags: true);
        client.Capture("third-distinct-id", "some-event", sendFeatureFlags: true);
        await client.FlushAsync();

        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "event": "some-event",
                           "properties": {
                             "distinct_id": "some-distinct-id",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$feature/flag1": true,
                             "$feature/flag2": false,
                             "$feature/flag3": "variant-0",
                             "$active_feature_flags": [
                               "flag1",
                               "flag3"
                             ]
                           },
                           "timestamp": "2024-01-21T19:08:23\u002B00:00"
                         },
                         {
                           "event": "some-event",
                           "properties": {
                             "distinct_id": "some-distinct-id",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$feature/flag1": true,
                             "$feature/flag2": false,
                             "$feature/flag3": "variant-0",
                             "$active_feature_flags": [
                               "flag1",
                               "flag3"
                             ]
                           },
                           "timestamp": "2024-01-21T19:08:23\u002B00:00"
                         },
                         {
                           "event": "some-event",
                           "properties": {
                             "distinct_id": "another-distinct-id",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$feature/flag1": true,
                             "$feature/flag2": false,
                             "$feature/flag3": "variant-1",
                             "$active_feature_flags": [
                               "flag1",
                               "flag3"
                             ]
                           },
                           "timestamp": "2024-01-21T19:08:23\u002B00:00"
                         },
                         {
                           "event": "some-event",
                           "properties": {
                             "distinct_id": "some-distinct-id",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$feature/flag1": true,
                             "$feature/flag2": false,
                             "$feature/flag3": "variant-0",
                             "$active_feature_flags": [
                               "flag1",
                               "flag3"
                             ]
                           },
                           "timestamp": "2024-01-21T19:08:23\u002B00:00"
                         },
                         {
                           "event": "some-event",
                           "properties": {
                             "distinct_id": "some-distinct-id",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$feature/flag1": true,
                             "$feature/flag2": false,
                             "$feature/flag3": "variant-0",
                             "$active_feature_flags": [
                               "flag1",
                               "flag3"
                             ]
                           },
                           "timestamp": "2024-01-21T19:08:23\u002B00:00"
                         },
                         {
                           "event": "some-event",
                           "properties": {
                             "distinct_id": "another-distinct-id",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$feature/flag1": true,
                             "$feature/flag2": false,
                             "$feature/flag3": "variant-1",
                             "$active_feature_flags": [
                               "flag1",
                               "flag3"
                             ]
                           },
                           "timestamp": "2024-01-21T19:08:23\u002B00:00"
                         },
                         {
                           "event": "some-event",
                           "properties": {
                             "distinct_id": "third-distinct-id",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$feature/flag1": true,
                             "$feature/flag2": false,
                             "$feature/flag3": "variant-2",
                             "$active_feature_flags": [
                               "flag1",
                               "flag3"
                             ]
                           },
                           "timestamp": "2024-01-21T19:08:23\u002B00:00"
                         }
                       ]
                     }
                     """, received);
    }
}
