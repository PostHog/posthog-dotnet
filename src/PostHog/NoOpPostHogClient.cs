using System.Text.Json;
using PostHog;
using PostHog.Api;
using PostHog.Features;
using PostHog.Json;

namespace PostHog.Sdk;

internal sealed class NoOpPostHogClient : IPostHogClient, IFeatureFlagEvaluationsHost
{
    static readonly IReadOnlyDictionary<string, FeatureFlag> EmptyFeatureFlags = new Dictionary<string, FeatureFlag>();

    NoOpPostHogClient()
    {
    }

    internal static NoOpPostHogClient Instance { get; } = new();

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

    public bool Capture(
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        FeatureFlagEvaluations? flags,
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

    public bool CaptureException(
        Exception exception,
        string distinctId,
        Dictionary<string, object>? properties,
        GroupCollection? groups,
        FeatureFlagEvaluations? flags,
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

    public Task<FeatureFlagEvaluations> EvaluateFlagsAsync(
        string distinctId,
        AllFeatureFlagsOptions? options,
        CancellationToken cancellationToken)
        => Task.FromResult(FeatureFlagEvaluations.Empty(this, distinctId));

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

    void IFeatureFlagEvaluationsHost.CaptureFeatureFlagCalled(
        string distinctId,
        string featureKey,
        EvaluatedFlagRecord? record,
        GroupCollection? groups,
        string? requestId,
        long? evaluatedAt,
        long? flagDefinitionsLoadedAt,
        IReadOnlyCollection<string> errors)
    {
    }

    void IFeatureFlagEvaluationsHost.LogFilterWarning(string message)
    {
    }
}
