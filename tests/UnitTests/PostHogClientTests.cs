using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    [Fact] // Similar to PostHog/posthog-python test_basic_identify
    public async Task SendsCorrectPayloadWithGeoIpEnabled()
    {
        var container = new TestContainer(false);
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
                           "$geoip_disable": false
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

    [Fact] // Ported from PostHog/posthog-python test_basic_group_identify
    public async Task SendsCorrectPayloadWithUserProvidedDistinctId()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddCaptureResponse();
        var client = container.Activate<PostHogClient>();

        var result = await client.GroupIdentifyAsync("custom_distinct_id", type: "organization", key: "id:5", "PostHog");

        Assert.Equal(1, result.Status);
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        Assert.Equal($$"""
                       {
                         "event": "$groupidentify",
                         "distinct_id": "custom_distinct_id",
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
        JsonAssert.EqualIgnoringUuids($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "some-event",
                           "distinct_id": "some-distinct-id",
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
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "some-event",
                           "distinct_id": "some-distinct-id",
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
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "some-event",
                           "distinct_id": "another-distinct-id",
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
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "some-event",
                           "distinct_id": "some-distinct-id",
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
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "some-event",
                           "distinct_id": "some-distinct-id",
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
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "some-event",
                           "distinct_id": "another-distinct-id",
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
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "some-event",
                           "distinct_id": "third-distinct-id",
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

    [Fact]
    public async Task CaptureWithCustomTimestampUsesProvidedTimestamp()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var customTimestamp = new DateTimeOffset(2023, 12, 25, 10, 30, 45, TimeSpan.Zero);
        client.Capture("test-user", "custom-timestamp-event", customTimestamp);
        await client.FlushAsync();

        var received = requestHandler.GetReceivedRequestBody(indented: true);

        JsonAssert.EqualIgnoringUuids($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "custom-timestamp-event",
                           "distinct_id": "test-user",
                           "properties": {
                             "timestamp": "2023-12-25T10:30:45\u002B00:00",
                             "distinct_id": "test-user",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true
                           },
                           "timestamp": "2023-12-25T10:30:45\u002B00:00"
                         }
                       ]
                     }
                     """, received);
    }

    [Fact]
    public async Task CaptureWithTimestampAndPropertiesUsesProvidedTimestamp()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var customTimestamp = new DateTimeOffset(2023, 12, 25, 10, 30, 45, TimeSpan.Zero);
        var properties = new Dictionary<string, object> { ["custom_prop"] = "custom_value" };
        client.Capture("test-user", "custom-timestamp-with-props", customTimestamp, properties);
        await client.FlushAsync();

        var received = requestHandler.GetReceivedRequestBody(indented: true);
        JsonAssert.EqualIgnoringUuids($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "custom-timestamp-with-props",
                           "distinct_id": "test-user",
                           "properties": {
                             "custom_prop": "custom_value",
                             "timestamp": "2023-12-25T10:30:45\u002B00:00",
                             "distinct_id": "test-user",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true
                           },
                           "timestamp": "2023-12-25T10:30:45\u002B00:00"
                         }
                       ]
                     }
                     """, received);
    }

    [Fact]
    public async Task CaptureWithTimestampAndGroupsUsesProvidedTimestamp()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var customTimestamp = new DateTimeOffset(2023, 12, 25, 10, 30, 45, TimeSpan.Zero);
        var groups = new GroupCollection { { "company", "acme-corp" } };
        client.Capture("test-user", "custom-timestamp-with-groups", customTimestamp, groups);
        await client.FlushAsync();

        var received = requestHandler.GetReceivedRequestBody(indented: true);
        JsonAssert.EqualIgnoringUuids($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "custom-timestamp-with-groups",
                           "distinct_id": "test-user",
                           "properties": {
                             "timestamp": "2023-12-25T10:30:45\u002B00:00",
                             "distinct_id": "test-user",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$groups": {
                               "company": "acme-corp"
                             }
                           },
                           "timestamp": "2023-12-25T10:30:45\u002B00:00"
                         }
                       ]
                     }
                     """, received);
    }

    [Fact]
    public async Task CaptureWithTimestampAndFeatureFlagsUsesProvidedTimestamp()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        container.FakeHttpMessageHandler.AddDecideResponse("""{"featureFlags": {"test-flag": true}}""");
        var client = container.Activate<PostHogClient>();

        var customTimestamp = new DateTimeOffset(2023, 12, 25, 10, 30, 45, TimeSpan.Zero);
        client.Capture("test-user", "custom-timestamp-with-flags", customTimestamp, sendFeatureFlags: true);
        await client.FlushAsync();

        var received = requestHandler.GetReceivedRequestBody(indented: true);
        JsonAssert.EqualIgnoringUuids($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "custom-timestamp-with-flags",
                           "distinct_id": "test-user",
                           "properties": {
                             "timestamp": "2023-12-25T10:30:45\u002B00:00",
                             "distinct_id": "test-user",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$feature/test-flag": true,
                             "$active_feature_flags": [
                               "test-flag"
                             ]
                           },
                           "timestamp": "2023-12-25T10:30:45\u002B00:00"
                         }
                       ]
                     }
                     """, received);
    }

    [Fact]
    public async Task CaptureWithTimestampPropertiesAndGroupsUsesProvidedTimestamp()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var customTimestamp = new DateTimeOffset(2023, 12, 25, 10, 30, 45, TimeSpan.Zero);
        var properties = new Dictionary<string, object> { ["custom_prop"] = "custom_value" };
        var groups = new GroupCollection { { "company", "acme-corp" } };
        client.Capture("test-user", "custom-timestamp-full", customTimestamp, properties, groups);
        await client.FlushAsync();

        var received = requestHandler.GetReceivedRequestBody(indented: true);
        JsonAssert.EqualIgnoringUuids($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "custom-timestamp-full",
                           "distinct_id": "test-user",
                           "properties": {
                             "custom_prop": "custom_value",
                             "timestamp": "2023-12-25T10:30:45\u002B00:00",
                             "distinct_id": "test-user",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$groups": {
                               "company": "acme-corp"
                             }
                           },
                           "timestamp": "2023-12-25T10:30:45\u002B00:00"
                         }
                       ]
                     }
                     """, received);
    }

    [Fact]
    public async Task CaptureWithAllParametersAndTimestampUsesProvidedTimestamp()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        container.FakeHttpMessageHandler.AddDecideResponse("""{"featureFlags": {"test-flag": true}}""");
        var client = container.Activate<PostHogClient>();

        var customTimestamp = new DateTimeOffset(2023, 12, 25, 10, 30, 45, TimeSpan.Zero);
        var properties = new Dictionary<string, object> { ["custom_prop"] = "custom_value" };
        var groups = new GroupCollection { { "company", "acme-corp" } };
        client.Capture("test-user", "custom-timestamp-all-params", customTimestamp, properties, groups, sendFeatureFlags: true);
        await client.FlushAsync();

        var received = requestHandler.GetReceivedRequestBody(indented: true);
        JsonAssert.EqualIgnoringUuids($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "custom-timestamp-all-params",
                           "distinct_id": "test-user",
                           "properties": {
                             "custom_prop": "custom_value",
                             "timestamp": "2023-12-25T10:30:45\u002B00:00",
                             "distinct_id": "test-user",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$groups": {
                               "company": "acme-corp"
                             },
                             "$feature/test-flag": true,
                             "$active_feature_flags": [
                               "test-flag"
                             ]
                           },
                           "timestamp": "2023-12-25T10:30:45\u002B00:00"
                         }
                       ]
                     }
                     """, received);
    }

    [Fact]
    public async Task CaptureWithTimestampParameterOverridesTimestampInProperties()
    {
        var container = new TestContainer();
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        var customTimestamp = new DateTimeOffset(2023, 12, 25, 10, 30, 45, TimeSpan.Zero);
        var existingTimestamp = new DateTimeOffset(2023, 11, 15, 14, 22, 33, TimeSpan.Zero);
        var properties = new Dictionary<string, object>
        {
            ["custom_prop"] = "custom_value",
            ["timestamp"] = existingTimestamp // This gets overridden by timestamp parameter
        };
        client.Capture("test-user", "timestamp-override", customTimestamp, properties);
        await client.FlushAsync();

        var received = requestHandler.GetReceivedRequestBody(indented: true);
        JsonAssert.EqualIgnoringUuids($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "timestamp-override",
                           "distinct_id": "test-user",
                           "properties": {
                             "custom_prop": "custom_value",
                             "timestamp": "2023-12-25T10:30:45\u002B00:00",
                             "distinct_id": "test-user",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true
                           },
                           "timestamp": "2023-12-25T10:30:45\u002B00:00"
                         }
                       ]
                     }
                     """, received);
    }

    [Fact]
    public async Task CaptureWithSendFeatureFlagsTrueAndLocalEvaluationEnabledUsesLocallyEvaluatedFlags()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
                "flags": [
                    {
                        "id": 1,
                        "name": "Local Flag",
                        "key": "local-flag",
                        "active": true,
                        "rollout_percentage": 100,
                        "filters": {
                            "groups": [
                                {
                                    "properties": [],
                                    "rollout_percentage": 100
                                }
                            ]
                        }
                    },
                    {
                        "id": 2,
                        "name": "Another Local Flag",
                        "key": "another-local-flag",
                        "active": true,
                        "rollout_percentage": 100,
                        "filters": {
                            "groups": [
                                {
                                    "properties": [],
                                    "rollout_percentage": 100
                                }
                            ]
                        }
                    }
                ]
            }
            """
        );

        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        // Preload local flags to ensure they are available
        await client.GetAllFeatureFlagsAsync("preload-user", options: null, CancellationToken.None);
        client.Capture("test-distinct-id", "test-event", sendFeatureFlags: true);
        await client.FlushAsync();

        var received = batchHandler.GetReceivedRequestBody(indented: true);
        JsonAssert.EqualIgnoringUuids($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "test-event",
                           "distinct_id": "test-distinct-id",
                           "properties": {
                             "distinct_id": "test-distinct-id",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true,
                             "$feature/local-flag": true,
                             "$feature/another-local-flag": true,
                             "$active_feature_flags": [
                               "local-flag",
                               "another-local-flag"
                             ]
                           },
                           "timestamp": "2024-01-21T19:08:23\u002B00:00"
                         }
                       ]
                     }
                     """, received);
    }

    [Fact]
    public async Task CaptureWithSendFeatureFlagsFalseDoesNotAddFeatureFlagsEvenWhenLocalEvaluationEnabled()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
                "flags": [
                    {
                        "id": 1,
                        "name": "Local Flag",
                        "key": "local-flag",
                        "active": true,
                        "rollout_percentage": 100,
                        "filters": {
                            "groups": [
                                {
                                    "properties": [],
                                    "rollout_percentage": 100
                                }
                            ]
                        }
                    }
                ]
            }
            """
        );

        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        // Preload local flags to ensure they are available
        await client.GetAllFeatureFlagsAsync("preload-user", options: null, CancellationToken.None);

        client.Capture("test-distinct-id", "test-event", sendFeatureFlags: false);
        await client.FlushAsync();

        var received = batchHandler.GetReceivedRequestBody(indented: true);
        JsonAssert.EqualIgnoringUuids($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "test-event",
                           "distinct_id": "test-distinct-id",
                           "properties": {
                             "distinct_id": "test-distinct-id",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true
                           },
                           "timestamp": "2024-01-21T19:08:23\u002B00:00"
                         }
                       ]
                     }
                     """, received);
    }

    [Fact]
    public async Task CaptureDefaultsToNotSendingFeatureFlagsEvenWhenLocalEvaluationEnabled()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 21, 19, 08, 23, TimeSpan.Zero));
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            """
            {
                "flags": [
                    {
                        "id": 1,
                        "name": "Local Flag",
                        "key": "local-flag",
                        "active": true,
                        "rollout_percentage": 100,
                        "filters": {
                            "groups": [
                                {
                                    "properties": [],
                                    "rollout_percentage": 100
                                }
                            ]
                        }
                    }
                ]
            }
            """
        );

        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        // Preload local flags to ensure they are available
        await client.GetAllFeatureFlagsAsync("preload-user", options: null, CancellationToken.None);

        // Capture event WITHOUT specifying sendFeatureFlags (should default to false)
        client.Capture("test-distinct-id", "test-event");
        await client.FlushAsync();

        var received = batchHandler.GetReceivedRequestBody(indented: true);
        JsonAssert.EqualIgnoringUuids($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "uuid": "00000000-0000-0000-0000-000000000000",
                           "event": "test-event",
                           "distinct_id": "test-distinct-id",
                           "properties": {
                             "distinct_id": "test-distinct-id",
                             "$lib": "posthog-dotnet",
                             "$lib_version": "{{VersionConstants.Version}}",
                             "$geoip_disable": true
                           },
                           "timestamp": "2024-01-21T19:08:23\u002B00:00"
                         }
                       ]
                     }
                     """, received);
    }
}

public class TheCaptureExceptionMethod
{
    [Fact]
    public async Task CaptureExceptionWithDivideByZeroException() // based on PostHog/posthog-python test_exception_capture
    {
        var (container, requestHandler, client) = CreateClient();

        try
        {
            // Deliberately cause a divide by zero exception
            var zero = 0;
            var result = 1 / zero;
        }
        catch (DivideByZeroException ex)
        {
            client.CaptureException(ex, "some-distinct-id");
            await client.FlushAsync();

            var received = requestHandler.GetReceivedRequestBody(indented: true);
            var (_, batchItem, props) = ParseSingleEvent(received);

            Assert.Equal("$exception", batchItem.GetProperty("event").GetString());
            Assert.Equal("System.DivideByZeroException", props.GetProperty("$exception_type").GetString());
            Assert.Contains("divide by zero", props.GetProperty("$exception_message").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("posthog-dotnet", props.GetProperty("$lib").GetString());
            Assert.Equal(VersionConstants.Version, props.GetProperty("$lib_version").GetString());

            var firstException = GetFirstException(props);
            Assert.Equal("System.DivideByZeroException", firstException.GetProperty("type").GetString());
            Assert.Contains("divide by zero", firstException.GetProperty("value").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("generic", firstException.GetProperty("mechanism").GetProperty("type").GetString());
            Assert.True(firstException.GetProperty("mechanism").GetProperty("handled").GetBoolean());

            var stacktrace = firstException.GetProperty("stacktrace");
            Assert.Equal("raw", stacktrace.GetProperty("type").GetString());

            var frames = GetStackFrames(firstException);
            Assert.NotEmpty(frames);
            Assert.Contains(frames, f =>
                f.TryGetProperty("filename", out var fn) &&
                fn.GetString()!.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(frames, f =>
                f.TryGetProperty("context_line", out var cl) &&
                cl.GetString()!.Contains("var result = 1 / zero;", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("2024-01-21T19:08:23+00:00", batchItem.GetProperty("timestamp").GetString());
        }
    }

    [Fact]
    public async Task CaptureExceptionWithAggregateException()
    {
        var (container, requestHandler, client) = CreateClient();

        try
        {
            var exceptions = new List<Exception>();

            try
            {
                var zero = 0;
                var result = 1 / zero;
            }
            catch (DivideByZeroException ex)
            {
                exceptions.Add(ex);
            }

            try
            {
                var list = new List<int> { 1, 2, 3 };
                var invalid = list[5];
            }
            catch (ArgumentOutOfRangeException ex)
            {
                exceptions.Add(ex);
            }

            throw new AggregateException("Multiple errors occurred", exceptions);
        }
        catch (AggregateException ex)
        {
            client.CaptureException(ex, "some-distinct-id");
            await client.FlushAsync();

            var received = requestHandler.GetReceivedRequestBody(indented: true);
            var (_, batchItem, props) = ParseSingleEvent(received);

            Assert.Equal("$exception", batchItem.GetProperty("event").GetString());
            Assert.Equal("System.AggregateException", props.GetProperty("$exception_type").GetString());
            Assert.Contains("multiple errors occurred", props.GetProperty("$exception_message").GetString(),
                StringComparison.OrdinalIgnoreCase);

            var exceptionsList = GetExceptionList(props);
            Assert.Equal(3, exceptionsList.Count);
            Assert.Equal("System.AggregateException", exceptionsList[0].GetProperty("type").GetString());

            var divideByZeroException = GetExceptionOfType(props, "System.DivideByZeroException");
            Assert.Contains("divide by zero", divideByZeroException.GetProperty("value").GetString(),
                StringComparison.OrdinalIgnoreCase);

            var argumentOutOfRangeException = GetExceptionOfType(props, "System.ArgumentOutOfRangeException");
            Assert.Contains("index was out of range", argumentOutOfRangeException.GetProperty("value").GetString(),
                StringComparison.OrdinalIgnoreCase);

            var frames = GetStackFrames(argumentOutOfRangeException);

            // Should have a frame with no filename because it's from a system library
            Assert.Contains(frames, f =>
                f.TryGetProperty("filename", out var fn) &&
                string.IsNullOrEmpty(fn.GetString()));

            Assert.Contains(frames, f =>
                f.TryGetProperty("context_line", out var cl) &&
                cl.GetString()!.Contains("var invalid = list[5];", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task CaptureExceptionWithInnerExceptions()
    {
        var (container, requestHandler, client) = CreateClient();

        try
        {
            try
            {
                var zero = 0;
                var result = 1 / zero;
            }
            catch (DivideByZeroException ex)
            {
                throw new InvalidOperationException("Higher level exception", ex);
            }
        }
        catch (InvalidOperationException ex)
        {
            client.CaptureException(ex, "some-distinct-id");
            await client.FlushAsync();

            var received = requestHandler.GetReceivedRequestBody(indented: true);
            var (_, batchItem, props) = ParseSingleEvent(received);

            Assert.Equal("$exception", batchItem.GetProperty("event").GetString());
            Assert.Equal("System.InvalidOperationException", props.GetProperty("$exception_type").GetString());
            Assert.Contains("higher level exception", props.GetProperty("$exception_message").GetString(),
                StringComparison.OrdinalIgnoreCase);

            var exceptionsList = GetExceptionList(props);
            Assert.Equal(2, exceptionsList.Count);
            Assert.Equal("System.InvalidOperationException", exceptionsList[0].GetProperty("type").GetString());
            Assert.Contains("higher level exception", exceptionsList[0].GetProperty("value").GetString(),
                StringComparison.OrdinalIgnoreCase);

            var divideByZeroException = GetExceptionOfType(props, "System.DivideByZeroException");
            Assert.Contains("divide by zero", divideByZeroException.GetProperty("value").GetString(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CaptureExceptionWhenNoStackTrace()
    {
        var (container, requestHandler, client) = CreateClient();

#pragma warning disable CA2201 // Do not raise reserved exception types
        var ex = new Exception("Test exception without stack trace");
#pragma warning restore CA2201
        client.CaptureException(ex, "some-distinct-id");
        await client.FlushAsync();

        var received = requestHandler.GetReceivedRequestBody(indented: true);
        var (_, batchItem, props) = ParseSingleEvent(received);

        Assert.Equal("$exception", batchItem.GetProperty("event").GetString());
        Assert.Equal("System.Exception", props.GetProperty("$exception_type").GetString());
        Assert.Contains("test exception without stack trace", props.GetProperty("$exception_message").GetString(),
            StringComparison.OrdinalIgnoreCase);

        var exceptionsList = GetExceptionList(props);
        Assert.Single(exceptionsList);
        Assert.Equal("System.Exception", exceptionsList[0].GetProperty("type").GetString());
        Assert.Equal("[]", exceptionsList[0].GetProperty("stacktrace").GetProperty("frames").ToString());
    }

    [Fact]
    public async Task CaptureExceptionCauseIOFailureEmptyContext()
    {
        FileStream? lockHandle = null;
        var (container, requestHandler, client) = CreateClient();

        try
        {
            try
            {
                var zero = 0;
                var result = 1 / zero;
            }
            catch (DivideByZeroException ex)
            {
                var st = new System.Diagnostics.StackTrace(ex, true);
                var path = st.GetFrames()
                    .Select(f => f?.GetFileName())
                    .FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));

                // Lock the source file exclusively so File.ReadAllLines(path) will throw IOException
                // and as result frames will not contain source code context
                lockHandle = new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.None);

                client.CaptureException(ex, "some-distinct-id");
                await client.FlushAsync();

                var received = requestHandler.GetReceivedRequestBody(indented: true);
                var (_, batchItem, props) = ParseSingleEvent(received);
                var divideByZeroException = GetExceptionOfType(props, "System.DivideByZeroException");
                var frames = GetStackFrames(divideByZeroException);

                Assert.True(File.Exists(path!));
                Assert.Equal("$exception", batchItem.GetProperty("event").GetString());
                AssertContextEmpty(frames[0]);
            }
        }
        finally
        {
            if (lockHandle != null)
            {
                await lockHandle.DisposeAsync();
            }
        }
    }

    // This test is pretty expensive because it dynamically compiles and loads an assembly.
    // Consider alternatives.
    [Fact]
    public async Task CaptureExceptionWithInvalidFilePathInStackFrame()
    {
        var (_, requestHandler, client) = CreateClient();

        var fakePath = @"fake_file.cs";
        var code =
            """
            using System;
            public static class Thrower
            {
                public static void Boom()
                {
                    int zero = 0;
                    var _ = 1 / zero;
                }
            }
            """;

        // Parse with the fake source path so PDB embeds it
        var parse = CSharpParseOptions.Default;
        var tree = CSharpSyntaxTree.ParseText(SourceText.From(code, Encoding.UTF8), parse, path: fakePath);
        var trustedPlatformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);

        var refs = trustedPlatformAssemblies
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => MetadataReference.CreateFromFile(g.First()));

        var comp = CSharpCompilation.Create(
            "ThrowerAsm",
            [tree],
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));

        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        var emit = comp.Emit(pe, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

        pe.Position = 0;
        pdb.Position = 0;

        var assemblyLoadContext = new AssemblyLoadContext("ThrowerCtx", isCollectible: true);
        var assembly = assemblyLoadContext.LoadFromStream(pe, pdb);
        var boom = assembly.GetType("Thrower")!.GetMethod("Boom", BindingFlags.Public | BindingFlags.Static)!;

        try
        {
            boom.Invoke(null, null);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is DivideByZeroException ex)
        {
            client.CaptureException(ex, "some-distinct-id");
            await client.FlushAsync();

            var (_, batchItem, props) = ParseSingleEvent(requestHandler.GetReceivedRequestBody(indented: true));

            var divideByZeroException = GetExceptionOfType(props, "System.DivideByZeroException");
            var frames = GetStackFrames(divideByZeroException);

            Assert.Equal("$exception", batchItem.GetProperty("event").GetString());
            Assert.Equal(fakePath, frames[0].GetProperty("filename").GetString());
            AssertContextEmpty(frames[0]);
        }
    }

    [Fact]
    public async Task CaptureExceptionWithDeepNesting()
    {
        var (container, requestHandler, client) = CreateClient();

        Exception CreateNestedException(int level)
        {
            if (level <= 1)
            {
                return new InvalidOperationException($"Innermost exception at level {level}");
            }
            try
            {
                throw CreateNestedException(level - 1);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return new InvalidOperationException($"Nested exception at level {level}", ex);
            }
        }

        var deepException = CreateNestedException(15);
        client.CaptureException(deepException, "some-distinct-id");
        await client.FlushAsync();
        var received = requestHandler.GetReceivedRequestBody(indented: true);
        var (_, _, props) = ParseSingleEvent(received);
        var exceptionsList = GetExceptionList(props);

        Assert.Equal(15, deepException.ToString().Split([" ---> "], StringSplitOptions.None).Length);
        // Check that we capture up to configured max depth + one top level exception
        Assert.Equal(5, exceptionsList.Count);
    }

    [Fact]
    public async Task CaptureExceptionWithCircularInnerReference()
    {
        var (_, requestHandler, client) = CreateClient();

        var ex1 = new InvalidOperationException("ex1");
        var ex2 = new InvalidOperationException("ex2", ex1);

        var fld = typeof(Exception).GetField("_innerException", BindingFlags.NonPublic | BindingFlags.Instance)
               ?? typeof(Exception).GetField("m_innerException", BindingFlags.NonPublic | BindingFlags.Instance);

        if (fld is null)
        {
            // runtime doesn't expose the field
            return;
        }

        fld.SetValue(ex1, ex2);

        client.CaptureException(ex1, "some-distinct-id");
        await client.FlushAsync();

        var received = requestHandler.GetReceivedRequestBody(indented: true);
        var (_, _, props) = ParseSingleEvent(received);
        var exceptionsList = GetExceptionList(props);

        Assert.Equal(2, exceptionsList.Count);
        Assert.Equal(ex1, ex2.InnerException);
        Assert.Equal(ex2, ex1.InnerException);
    }

    [Fact]
    public async Task CaptureExceptionWithLargeAggregateException()
    {
        var (container, requestHandler, client) = CreateClient();
        var innerExceptions = new List<Exception>();

        for (int i = 0; i < 150; i++)
        {
            innerExceptions.Add(new InvalidOperationException($"Inner exception {i + 1}"));
        }

        var aggEx = new AggregateException("Aggregate with many inner exceptions", innerExceptions);
        client.CaptureException(aggEx, "some-distinct-id");
        await client.FlushAsync();

        var received = requestHandler.GetReceivedRequestBody(indented: true);
        var (_, _, props) = ParseSingleEvent(received);
        var exceptionsList = GetExceptionList(props);

        // Check that we capture up to configured max exceptions + one top level exception
        Assert.Equal(51, exceptionsList.Count);
    }

    [Fact]
    public async Task CaptureExceptionWithLargeStackTrace()
    {
        Exception CreateDeepStack(int depth)
        {
            try
            {
                Frame(depth);
                throw new InvalidOperationException("Unreachable");
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return ex;
            }

            static void Frame(int n)
            {
                if (n == 0)
                {
                    ThrowLeaf();
                }
                else
                {
                    Frame(n - 1);
                }
            }

            static void ThrowLeaf() =>
                throw new InvalidOperationException("Deep stack reached");
        }

        var (container, requestHandler, client) = CreateClient();

        var deepException = CreateDeepStack(300);
        client.CaptureException(deepException, "some-distinct-id");
        await client.FlushAsync();

        var received = requestHandler.GetReceivedRequestBody(indented: true);
        var (_, _, props) = ParseSingleEvent(received);
        var firstException = GetFirstException(props);
        var frames = GetStackFrames(firstException);

        // Check that we capture up to configured max stack frames
        Assert.Equal(50, frames.Count);
    }

    private static (TestContainer container, FakeHttpMessageHandler.RequestHandler requestHandler, PostHogClient client)
        CreateClient(DateTimeOffset? now = null)
    {
        var container = new TestContainer();

        if (now is DateTimeOffset dt)
        {
            container.FakeTimeProvider.SetUtcNow(dt);
        }

        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();

        return (container, requestHandler, client);
    }

    private static (JsonElement root, JsonElement batchItem, JsonElement props)
        ParseSingleEvent(string jsonString)
    {
        var doc = JsonDocument.Parse(jsonString);
        var root = doc.RootElement;
        var batchItem = root.GetProperty("batch").EnumerateArray().Single();
        var props = batchItem.GetProperty("properties");

        return (root, batchItem, props);
    }

    private static List<JsonElement> GetExceptionList(JsonElement props)
        => [.. props.GetProperty("$exception_list").EnumerateArray()];

    private static JsonElement GetFirstException(JsonElement props)
        => GetExceptionList(props).First();

    private static JsonElement GetExceptionOfType(JsonElement props, string type)
        => GetExceptionList(props).First(e => e.GetProperty("type").GetString() == type);

    private static List<JsonElement> GetStackFrames(JsonElement exceptionObj)
        => [.. exceptionObj.GetProperty("stacktrace").GetProperty("frames").EnumerateArray()];

    private static void AssertContextEmpty(JsonElement frame)
    {
        Assert.Equal("[]", frame.GetProperty("pre_context").ToString());
        Assert.Equal("", frame.GetProperty("context_line").GetString());
        Assert.Equal("[]", frame.GetProperty("post_context").ToString());
    }
}

public class TheLoadFeatureFlagsAsyncMethod
{
    [Fact]
    public async Task LoadsFeatureFlagsSuccessfully()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse("""{"flags": []}""");
        var client = container.Activate<PostHogClient>();

        await client.LoadFeatureFlagsAsync();

        // Verify info log was recorded
        var infoLogs = container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Information);
        Assert.Contains(infoLogs, log => log.Message?.Contains("Loading feature flags for local evaluation", StringComparison.Ordinal) == true);

        // Verify debug log was recorded
        var debugLogs = container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Debug);
        Assert.Contains(debugLogs, log => log.Message?.Contains("Feature flags loaded successfully", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task LogsWarningWhenPersonalApiKeyIsNull()
    {
        var container = new TestContainer(); // No personal API key
        var client = container.Activate<PostHogClient>();

        await client.LoadFeatureFlagsAsync();

        // Verify warning was logged
        var warningLogs = container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Warning);
        Assert.Contains(warningLogs, log =>
            log.Message?.Contains("You have to specify a personal_api_key to use feature flags", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task LogsDebugWhenFlagsLoadedSuccessfully()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse("""{"flags": []}""");
        var client = container.Activate<PostHogClient>();

        await client.LoadFeatureFlagsAsync();

        // Verify debug log was recorded
        var debugLogs = container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Debug);
        Assert.Contains(debugLogs, log => log.Message?.Contains("Feature flags loaded successfully", StringComparison.Ordinal) == true);
    }

    [Fact(Skip = "Cancellation token handling needs integration test")]
    public async Task RespectsCancellationToken()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        // Add a handler that will throw OperationCanceledException
        var uri = new Uri("https://us.i.posthog.com/api/feature_flag/local_evaluation?token=fake-project-api-key&send_cohorts");
        container.FakeHttpMessageHandler.AddResponseException(uri, HttpMethod.Get, new OperationCanceledException());
        var client = container.Activate<PostHogClient>();

        using var cts = new CancellationTokenSource();
#pragma warning disable CA1849 // Call async methods when available
        cts.Cancel();
#pragma warning restore CA1849

        // Should throw OperationCanceledException
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.LoadFeatureFlagsAsync(cts.Token));
    }

    [Fact(Skip = "Cancellation token handling needs integration test")]
    public async Task DoesNotLogErrorForCancellation()
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        // Add a handler that will throw OperationCanceledException
        var uri = new Uri("https://us.i.posthog.com/api/feature_flag/local_evaluation?token=fake-project-api-key&send_cohorts");
        container.FakeHttpMessageHandler.AddResponseException(uri, HttpMethod.Get, new OperationCanceledException());
        var client = container.Activate<PostHogClient>();

        using var cts = new CancellationTokenSource();
#pragma warning disable CA1849 // Call async methods when available
        cts.Cancel();
#pragma warning restore CA1849

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.LoadFeatureFlagsAsync(cts.Token));

        // Verify no error was logged for cancellation
        var errorLogs = container.FakeLoggerProvider.GetAllEvents(minimumLevel: LogLevel.Error);
        Assert.DoesNotContain(errorLogs, log =>
            log.Message?.Contains("Failed to load feature flags", StringComparison.Ordinal) == true);
    }
}
