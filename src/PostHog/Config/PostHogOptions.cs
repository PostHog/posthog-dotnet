using Microsoft.Extensions.Options;
using PostHog.Api;
using PostHog.Library;
using PostHog.Versioning;

namespace PostHog;

/// <summary>
/// Options for configuring the PostHog client.
/// </summary>
public sealed class PostHogOptions : IOptions<PostHogOptions>
{
    string? _projectToken;
    string? _projectApiKey;
    string? _secretKey;
    string? _personalApiKey;

    internal string LibraryName { get; set; } = PostHogApiClient.LibraryName;

    internal string LibraryVersion { get; set; } = VersionConstants.Version;

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

    internal (string? ProjectToken, string? ProjectApiKey) GetProjectKeyState() =>
        (_projectToken, _projectApiKey);

    internal void RestoreProjectKeyState(
        (string? ProjectToken, string? ProjectApiKey) state,
        bool restoreProjectToken,
        bool restoreProjectApiKey)
    {
        if (restoreProjectToken)
        {
            _projectToken = state.ProjectToken;
        }
        if (restoreProjectApiKey)
        {
            _projectApiKey = state.ProjectApiKey;
        }
    }

    internal void Normalize()
    {
        _projectToken = _projectToken.NullIfEmpty();
        _projectApiKey = _projectApiKey.NullIfEmpty();
        _secretKey = _secretKey.NullIfEmpty();
        _personalApiKey = _personalApiKey.NullIfEmpty();
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
    /// Optional secret key used for local feature flag evaluation and remote config. Accepts either a Personal
    /// API Key (<c>phx_...</c>) or a Project Secret API Key (<c>phs_...</c>).
    /// </summary>
    /// <remarks>
    /// You can find these at https://us.posthog.com/project/{YOUR_PROJECT_ID}/settings/user-api-keys
    /// When developing an ASP.NET Core project locally, we recommend setting this in your user secrets.
    /// <c>
    /// dotnet user-secrets --project your/project/path.csproj set PostHog:SecretKey YOUR_SECRET_KEY
    /// </c>
    /// In other cases, use an appropriate secrets manager, configuration provider, or environment variable.
    /// When both <see cref="SecretKey"/> and the deprecated <see cref="PersonalApiKey"/> are set,
    /// <see cref="SecretKey"/> takes precedence.
    /// </remarks>
    public string? SecretKey
    {
        get => _secretKey ?? _personalApiKey;
        set => _secretKey = value;
    }

    /// <summary>
    /// Obsolete alias for <see cref="SecretKey"/>.
    /// </summary>
    [Obsolete("Use SecretKey instead. SecretKey accepts a Personal API Key or a Project Secret API Key.")]
    public string? PersonalApiKey
    {
        get => _secretKey ?? _personalApiKey;
        set => _personalApiKey = value;
    }

    /// <summary>
    /// Evaluation contexts for feature flags.
    /// </summary>
    /// <remarks>
    /// When set, only feature flags with no evaluation contexts or at least one matching context will be evaluated
    /// for this SDK instance. Use this to isolate flags by application, platform, or product area.
    /// </remarks>
    public IReadOnlyList<string>? EvaluationContexts { get; set; }

    /// <summary>
    /// Whether captured events are attributed to a server. (Default: true)
    /// </summary>
    /// <remarks>
    /// When <c>true</c> (the default), every captured event includes the <c>$is_server</c> property set to
    /// <c>true</c> so PostHog identifies the event as server-side. Set this to <c>false</c> when using the SDK
    /// as a client or CLI so the device's operating system is attributed normally instead of being treated as a
    /// server. When <c>false</c>, the <c>$is_server</c> property is omitted entirely.
    /// <code>
    /// var options = new PostHogOptions { ProjectToken = "phc_...", IsServer = false };
    /// </code>
    /// </remarks>
    public bool IsServer { get; set; } = true;

    /// <summary>
    /// Whether this client is disabled and should no-op instead of sending data to PostHog. (Default: false)
    /// </summary>
    /// <remarks>
    /// Read once during <see cref="PostHogClient"/> construction. Mutating this after the client is
    /// activated has no effect. Re-create the client to change the disabled state.
    /// </remarks>
    public bool Disabled { get; set; }

    /// <summary>
    /// PostHog API host, usually 'https://us.i.posthog.com' (default) or 'https://eu.i.posthog.com'
    /// </summary>
    public Uri HostUrl { get; set; } = new("https://us.i.posthog.com");

    /// <summary>
    /// Your PostHog app host, such as <c>https://posthog.example.com</c>. <see cref="PostHogClient.Emails"/> sends
    /// there.
    /// </summary>
    /// <remarks>
    /// Not needed on PostHog Cloud: a cloud <see cref="HostUrl"/>, such as <c>https://eu.i.posthog.com</c>, maps to
    /// its region's app host. Any other <see cref="HostUrl"/> may be a reverse proxy for ingestion, which must not
    /// receive <see cref="SecretKey"/>, so without this option email requests fail with <c>missing_app_host</c>.
    /// Self-hosted instances set it. It must use https unless <see cref="AllowInsecureAppHost"/> is set.
    /// </remarks>
    public Uri? AppHost { get; set; }

    /// <summary>
    /// Lets <see cref="PostHogClient.Emails"/> send to an http <see cref="AppHost"/>, such as
    /// <c>http://localhost:8010</c> during local development. (Default: <c>false</c>)
    /// </summary>
    /// <remarks>
    /// Email requests carry <see cref="SecretKey"/> and the email itself, so over http both travel unencrypted.
    /// Without this option an http <see cref="AppHost"/> fails with <c>insecure_app_host</c> before any request.
    /// </remarks>
    public bool AllowInsecureAppHost { get; set; }

    /// <summary>
    /// The timeout for each attempt of an <see cref="PostHogClient.Emails"/> request to PostHog. (Default: 15 seconds)
    /// </summary>
    /// <remarks>
    /// An attachment's upload to PostHog's file storage can take longer for a large file, so it runs until the
    /// <see cref="HttpClient"/>'s own timeout instead: 100 seconds unless your app changed it, so raise
    /// <see cref="HttpClient.Timeout"/> when your app sends large files over a slow link. Uploads go through an
    /// <see cref="HttpClient"/> of their own, named <c>PostHogClient.Uploads</c>, that never follows a redirect and
    /// carries none of your app's handlers or default headers. It uses the system proxy.
    /// </remarks>
    public TimeSpan EmailRequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How many times an <see cref="PostHogClient.Emails"/> request is retried after a network error, a rate limit,
    /// an idempotency key still in flight, or an unavailable service. (Default: 2)
    /// </summary>
    /// <remarks>
    /// Set it to <c>0</c> when a job runner already retries the send, and pass an idempotency key that names the
    /// email, such as <c>welcome:user_123</c>.
    /// </remarks>
    public int EmailRequestMaxRetries { get; set; } = 2;

    /// <summary>
    /// The email provider your email moved from, such as <c>resend</c>. Sent with every
    /// <see cref="PostHogClient.Emails"/> request, so PostHog knows where your sends came from.
    /// </summary>
    public string? MigratedFrom { get; set; }

    /// <summary>
    /// Default properties to send when capturing events. These properties override any properties with the same
    /// key sent with the event.
    /// </summary>
    public Dictionary<string, object> SuperProperties { get; init; } = new();

    /// <summary>
    /// Optional callback invoked after an event is fully enriched and before it is serialized for upload.
    /// Return the event (mutated or unchanged) to continue, or <c>null</c> to drop it.
    /// </summary>
    public Func<CapturedEvent, CapturedEvent?>? BeforeSend { get; set; }

    /// <summary>
    /// When <see cref="SecretKey"/> is set, this is the interval to poll for feature flags used in
    /// local evaluation. Default is 30 seconds.
    /// </summary>
    public TimeSpan FeatureFlagPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a prompt fetched by <see cref="PostHogClient.Prompts"/> is served from the cache before it is fetched
    /// again. (Default: 5 minutes)
    /// </summary>
    /// <remarks>
    /// Each <c>GetAsync</c> call can override it. <see cref="TimeSpan.Zero"/> fetches on every call. Prompt fetches
    /// are rate limited per key, so a short TTL on a busy service can hit that limit.
    /// </remarks>
    public TimeSpan PromptCacheTtl { get; set; } = TimeSpan.FromMinutes(5);

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
    /// The max number of messages to store in the queue before we start dropping messages. (Default: 10000)
    /// </summary>
    /// <remarks>
    /// This property prevents runaway growth of the queue in the case of network outage or a burst of messages.
    /// </remarks>
    public int MaxQueueSize { get; set; } = 10_000;

    /// <summary>
    /// The number of events to queue before sending to PostHog (Default: 100)
    /// </summary>
    public int FlushAt { get; set; } = 100;

    /// <summary>
    /// The interval between periodic flushes. (Default: 5s)
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The maximum number of retries for failed requests. (Default: 3)
    /// </summary>
    /// <remarks>
    /// Retries are performed for transient failures such as 5xx errors, 408 (Request Timeout),
    /// and 429 (Too Many Requests).
    /// </remarks>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// The maximum number of retries for feature flag requests after transient network errors
    /// or HTTP 502/504 responses. (Default: 1)
    /// Set to 0 to disable feature flag request retries.
    /// </summary>
    public int FeatureFlagRequestMaxRetries { get; set; } = 1;

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