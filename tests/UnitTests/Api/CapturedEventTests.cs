using PostHog.Api;

namespace CapturedEventTests;

public class TheConstructor
{
    [Fact]
    public void GeneratesUniqueUuidForEachEvent()
    {
        var event1 = new CapturedEvent("event", "user-1", null, DateTimeOffset.UtcNow);
        var event2 = new CapturedEvent("event", "user-1", null, DateTimeOffset.UtcNow);
        var event3 = new CapturedEvent("event", "user-2", null, DateTimeOffset.UtcNow);

        Assert.NotEqual(event1.Uuid, event2.Uuid);
        Assert.NotEqual(event1.Uuid, event3.Uuid);
        Assert.NotEqual(event2.Uuid, event3.Uuid);
    }

    [Fact]
    public void GeneratesValidGuidFormat()
    {
        var capturedEvent = new CapturedEvent("test-event", "test-user", null, DateTimeOffset.UtcNow);

        Assert.True(Guid.TryParse(capturedEvent.Uuid, out var guid), "UUID should be valid GUID format");
        Assert.NotEqual(Guid.Empty, guid);
    }

    [Fact]
    public void SetsDistinctIdAsTopLevelProperty()
    {
        var distinctId = "my-distinct-id";
        var capturedEvent = new CapturedEvent("test-event", distinctId, null, DateTimeOffset.UtcNow);

        Assert.Equal(distinctId, capturedEvent.DistinctId);
    }

    [Fact]
    public void SetsDistinctIdInPropertiesForApiCompatibility()
    {
        var distinctId = "my-distinct-id";
        var capturedEvent = new CapturedEvent("test-event", distinctId, null, DateTimeOffset.UtcNow);

        Assert.True(capturedEvent.Properties.ContainsKey("distinct_id"));
        Assert.Equal(distinctId, capturedEvent.Properties["distinct_id"]);
    }

    [Fact]
    public void SetsEventName()
    {
        var capturedEvent = new CapturedEvent("my-event", "user-1", null, DateTimeOffset.UtcNow);

        Assert.Equal("my-event", capturedEvent.EventName);
    }

    [Fact]
    public void SetsTimestamp()
    {
        var timestamp = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var capturedEvent = new CapturedEvent("test-event", "user-1", null, timestamp);

        Assert.Equal(timestamp, capturedEvent.Timestamp);
    }

    [Fact]
    public void PreservesProvidedProperties()
    {
        var properties = new Dictionary<string, object>
        {
            ["custom_prop"] = "custom_value",
            ["number_prop"] = 42
        };
        var capturedEvent = new CapturedEvent("test-event", "user-1", properties, DateTimeOffset.UtcNow);

        Assert.Equal("custom_value", capturedEvent.Properties["custom_prop"]);
        Assert.Equal(42, capturedEvent.Properties["number_prop"]);
    }

    [Fact]
    public void AddsLibraryProperties()
    {
        var capturedEvent = new CapturedEvent("test-event", "user-1", null, DateTimeOffset.UtcNow);

        Assert.True(capturedEvent.Properties.ContainsKey("$lib"));
        Assert.Equal("posthog-dotnet", capturedEvent.Properties["$lib"]);
        Assert.True(capturedEvent.Properties.ContainsKey("$lib_version"));
    }

    [Fact]
    public void SetsGeoIpDisableToTrueByDefault()
    {
        var capturedEvent = new CapturedEvent("test-event", "user-1", null, DateTimeOffset.UtcNow);

        Assert.True(capturedEvent.Properties.ContainsKey("$geoip_disable"));
        Assert.Equal(true, capturedEvent.Properties["$geoip_disable"]);
    }

    [Fact]
    public void PreservesExistingGeoIpDisableSetting()
    {
        var properties = new Dictionary<string, object>
        {
            ["$geoip_disable"] = false
        };
        var capturedEvent = new CapturedEvent("test-event", "user-1", properties, DateTimeOffset.UtcNow);

        Assert.Equal(false, capturedEvent.Properties["$geoip_disable"]);
    }

    [Fact]
    public void GeneratesMultipleUniqueUuidsInBulk()
    {
        const int count = 1000;
        var uuids = new HashSet<string>();

        for (int i = 0; i < count; i++)
        {
            var capturedEvent = new CapturedEvent("event", $"user-{i}", null, DateTimeOffset.UtcNow);
            uuids.Add(capturedEvent.Uuid);
        }

        Assert.Equal(count, uuids.Count); // All UUIDs should be unique
    }
}
