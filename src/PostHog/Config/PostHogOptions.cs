using Microsoft.Extensions.Options;
using PostHog.Library;

namespace PostHog;

/// <summary>
/// Options for configuring the PostHog client.
/// </summary>
public sealed class PostHogOptions : IOptions<PostHogOptions>
{
    string? _projectToken;
    string? _projectApiKey;

    /// <summary>
    /// The PostHog project token that identifies which project this client works with.
    /// </summary>
    /// <remarks>
    /// You can find this at https://us.posthog.com/settings/project-details#variables
    ///
    /// This field was formerly named <see cref="ProjectApiKey"/>.
    /// </remarks>
    public string? ProjectToken
    {
        get => _projectToken ?? _projectApiKey;
        set => _projectToken = value;
    }

    internal bool HasLegacyProjectApiKey => _projectApiKey is not null;

    internal void Normalize()
    {
        _projectToken = _projectToken.NullIfEmpty();
        _projectApiKey = _projectApiKey.NullIfEmpty();
        PersonalApiKey = PersonalApiKey.NullIfEmpty();
        HostUrl = HostUrl.NormalizeHostUrl();
    }

    /// <summary>
    /// Obsolete alias for <see cref="ProjectToken"/>.
    /// </summary>
    [Obsolete("Use ProjectToken instead. This will be removed in the next major version.")]
    public string? ProjectApiKey
    {
        get => _projectToken ?? _projectApiKey;
        set => _projectApiKey = value;
    }

    /// <summary>
    /// Optional personal API key for local feature flag evaluation.
    /// </summary>
    /// <remarks>
    /// You can find this https://us.posthog.com/project/{YOUR_PROJECT_ID}/settings/user-api-keys
    /// When developing an ASP.NET Core project locally, we recommend setting this in your user secrets.
    /// <c>
    /// dotnet user-secrets --project your/project/path.csproj set PostHog:PersonalApiKey YOUR_PERSONAL_API_KEY
    /// </c>
    /// In other cases, use an appropriate secrets manager, configuration provider, or environment variable.
    /// </remarks>
    public string? PersonalApiKey { get; set; }

    /// <summary>
    /// Whether this client is disabled and should no-op instead of sending data to PostHog. (Default: false)
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// PostHog API host, usually 'https://us.i.posthog.com' (default) or 'https://eu.i.posthog.com'
    /// </summary>
    public Uri HostUrl { get; set; } = new("https://us.i.posthog.com");

    /// <summary>
    /// Default properties to send when capturing events. These properties override any properties with the same
    /// key sent with the event.
    /// </summary>
    public Dictionary<string, object> SuperProperties { get; init; } = new();

    /// <summary>
    /// When <see cref="PersonalApiKey"/> is set, this is the interval to poll for feature flags used in
    /// local evaluation. Default is 30 seconds.
    /// </summary>
    public TimeSpan FeatureFlagPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The size limit of the $feature_flag_sent cache.
    /// </summary>
    /// <remarks>
    /// When evaluating a feature flag and <c>sendFeatureFlagEvents</c> is <c>true</c>, the client captures a
    /// $feature_flag_called event. To limit the cost to the customer, it only sends this event once per
    /// feature flag/distinct_id combination. To do this, it caches the sent events. This property sets the
    /// the size limit of that cache.
    /// </remarks>
    public long FeatureFlagSentCacheSizeLimit { get; set; } = 50_000;

    /// <summary>
    /// Gets the amount (as a percentage) the cache should be compacted when it reaches its size limit.
    /// </summary>
    public double FeatureFlagSentCacheCompactionPercentage { get; set; } = 0.2; // 20%

    /// <summary>
    /// Sets a sliding expiration for the $feature_flag_sent cache. See <see cref="FeatureFlagSentCacheSizeLimit"/>
    /// for more about the cache.
    /// </summary>
    public TimeSpan FeatureFlagSentCacheSlidingExpiration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// When <c>true</c> (default), the SDK emits warning logs from the
    /// <see cref="Features.FeatureFlagEvaluations"/> snapshot helpers — specifically when
    /// <see cref="Features.FeatureFlagEvaluations.OnlyAccessed"/> is called before any flags have been accessed,
    /// or when <see cref="Features.FeatureFlagEvaluations.Only(System.Collections.Generic.IEnumerable{string})"/>
    /// is given keys that are not present in the snapshot. Set to <c>false</c> to silence these warnings.
    /// </summary>
    public bool FeatureFlagsLogWarnings { get; set; } = true;

    /// <summary>
    /// The maximum number of messages to send in a batch. (Default: 100)
    /// </summary>
    /// <remarks>
    /// This property ensures we don't try to send too much data in a single batch request.
    /// </remarks>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// The max number of messages to store in the queue before we start dropping messages. (Default: 1000)
    /// </summary>
    /// <remarks>
    /// This property prevents runaway growth of the queue in the case of network outage or a burst of messages.
    /// </remarks>
    public int MaxQueueSize { get; set; } = 1000;

    /// <summary>
    /// The number of events to queue before sending to PostHog (Default: 20)
    /// </summary>
    public int FlushAt { get; set; } = 20;

    /// <summary>
    /// The interval in milliseconds between periodic flushes. (Default: 30s)
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The maximum number of retries for failed requests. (Default: 3)
    /// </summary>
    /// <remarks>
    /// Retries are performed for transient failures such as 5xx errors, 408 (Request Timeout),
    /// and 429 (Too Many Requests).
    /// </remarks>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// The initial delay between retries. (Default: 1 second)
    /// </summary>
    /// <remarks>
    /// The delay is doubled after each retry (exponential backoff).
    /// </remarks>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The maximum delay between retries. (Default: 30 seconds)
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to enable gzip compression for batch requests. (Default: true)
    /// </summary>
    /// <remarks>
    /// When enabled, batch requests will be compressed with gzip before sending,
    /// which reduces bandwidth usage and improves performance.
    /// </remarks>
    public bool EnableCompression { get; set; } = true;

    // Explicit implementation to hide this value from most users.
    // This is here to make it easier to instantiate the client with the options.
    PostHogOptions IOptions<PostHogOptions>.Value => this;
}