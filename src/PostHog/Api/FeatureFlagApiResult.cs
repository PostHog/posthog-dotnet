using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using PostHog.Json;

namespace PostHog.Api;

/**
 * This is a subset of the total possible properties on a response to remoteconfig/decide
 * Some properties are unnecessary for a backend SDK and are ignored
 */
public class FeatureFlagsApiResult
{
    public FeatureFlagsConfig Config { get; set; } = new(false);
    public bool IsAuthenticated { get; set; }
    public ReadOnlyDictionary<string, StringOrValue<bool>> FeatureFlags { get; set; } = new(new Dictionary<string, StringOrValue<bool>>());
    public Analytics Analytics { get; set; } = new(string.Empty);
    public bool DefaultIdentifiedOnly { get; set; }
    public bool ErrorsWhileComputingFlags { get; set; }
    public ReadOnlyDictionary<string, string> FeatureFlagPayloads { get; set; } = new(new Dictionary<string, string>());
}

public class FeatureFlagsConfig(bool enableCollectEverything)
{
    [JsonPropertyName("enable_collect_everything")]
    public bool EnableCollectEverything => enableCollectEverything;
}

public class Analytics(string endpoint)
{
    public string Endpoint => endpoint;
}
