using System.Runtime.InteropServices;
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

        var result = await client.GroupIdentifyAsync(type: "organization", key: "id:5", "PostHog", distinctId: "custom_distinct_id");

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

        Assert.Equal($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "event": "custom-timestamp-event",
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
        Assert.Equal($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "event": "custom-timestamp-with-props",
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
        Assert.Equal($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "event": "custom-timestamp-with-groups",
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
        Assert.Equal($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "event": "custom-timestamp-with-flags",
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
        Assert.Equal($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "event": "custom-timestamp-full",
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
        Assert.Equal($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "event": "custom-timestamp-all-params",
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
        Assert.Equal($$"""
                     {
                       "api_key": "fake-project-api-key",
                       "historical_migrations": false,
                       "batch": [
                         {
                           "event": "timestamp-override",
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
