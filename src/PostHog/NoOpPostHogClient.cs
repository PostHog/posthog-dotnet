using System.Diagnostics;
using System.Text.Json;
using PostHog.Api;
using PostHog.Features;
using PostHog.Json;

namespace PostHog;

internal sealed class NoOpPostHogClient : IPostHogClient
{
    static readonly IReadOnlyDictionary<string, FeatureFlag> EmptyFeatureFlags = new Dictionary<string, FeatureFlag>();
    static int _loggedNoDefaultClient;

    NoOpPostHogClient()
    {
    }

    internal static NoOpPostHogClient Instance { get; } = new();

    internal static void LogNoDefaultClient()
    {
        if (Interlocked.Exchange(ref _loggedNoDefaultClient, 1) == 0)
        {
            Trace.TraceWarning(
                "PostHogSdk.DefaultClient is not configured. PostHogSdk calls will be ignored until a default client is configured.");
        }
    }

    public Task<ApiResult> AliasAsync(string previousId, string newId, CancellationToken cancellationToken)
        => Task.FromResult(new ApiResult(0));

    public Task<ApiResult> IdentifyAsync(
        string distinctId,
        Dictionary<string, object>? personPropertiesToSet,
        Dictionary<string, object>? personPropertiesToSetOnce,
        CancellationToken cancellationToken)
        => Task.FromResult(new ApiResult(0));

    public Task<ApiResult> GroupIdentifyAsync(
        string type,
        StringOrValue<int> key,
        Dictionary<string, object>? properties,
        CancellationToken cancellationToken)
        => Task.FromResult(new ApiResult(0));

    public Task<ApiResult> GroupIdentifyAsync(
        string distinctId,
        string type,
        StringOrValue<int> key,
        Dictionary<string, object>? properties,
        CancellationToken cancellationToken)
        => Task.FromResult(new ApiResult(0));

    public bool Capture(
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        bool sendFeatureFlags,
        DateTimeOffset? timestamp = null)
        => false;

    public bool CaptureException(
        Exception exception,
        string distinctId,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        bool sendFeatureFlags,
        DateTimeOffset? timestamp = null)
        => false;

    public Task<bool> IsFeatureEnabledAsync(
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options,
        CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task<FeatureFlag?> GetFeatureFlagAsync(
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options,
        CancellationToken cancellationToken)
        => Task.FromResult<FeatureFlag?>(null);

    public Task<JsonDocument?> GetRemoteConfigPayloadAsync(string key, CancellationToken cancellationToken)
        => Task.FromResult<JsonDocument?>(null);

    public Task<IReadOnlyDictionary<string, FeatureFlag>> GetAllFeatureFlagsAsync(
        string distinctId,
        AllFeatureFlagsOptions? options,
        CancellationToken cancellationToken)
        => Task.FromResult(EmptyFeatureFlags);

    public Task LoadFeatureFlagsAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task FlushAsync()
        => Task.CompletedTask;

    public string Version => Versioning.VersionConstants.Version;

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
        => default;
}
