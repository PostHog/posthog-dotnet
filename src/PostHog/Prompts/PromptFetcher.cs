using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PostHog.Library;
using static PostHog.Library.Ensure;
#if NETSTANDARD2_0 || NETSTANDARD2_1
using PostHog.Library.Polyfills;
#endif

namespace PostHog.Prompts;

/// <summary>
/// Fetches prompts from the PostHog prompts API and caches them per name, version and label.
/// </summary>
internal sealed class PromptFetcher : IPostHogPrompts
{
    const string PromptsPath = "/api/environments/@current/llm_prompts/name/";

    // After a refetch fails, the stale entry is served without another request for this long,
    // or for the Retry-After the server sent, capped so a bad header cannot park a prompt for days.
    static readonly TimeSpan DefaultRefetchCooldown = TimeSpan.FromSeconds(60);
    static readonly TimeSpan MaxRefetchCooldown = TimeSpan.FromHours(1);

    readonly HttpClient _httpClient;
    readonly PostHogOptions _options;
    readonly bool _isDisabled;
    readonly TimeProvider _timeProvider;
    readonly ILogger<PromptFetcher> _logger;
    readonly ConcurrentDictionary<PromptCacheKey, CachedPrompt> _cache = new();
    readonly ConcurrentDictionary<PromptCacheKey, Lazy<Task<CachedPrompt>>> _inFlight = new();

    public PromptFetcher(
        HttpClient httpClient,
        PostHogOptions options,
        bool isDisabled,
        TimeProvider timeProvider,
        ILogger<PromptFetcher> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _isDisabled = isDisabled;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PromptResult> GetAsync(
        string name,
        GetPromptOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(NotNull(name)))
        {
            throw new ArgumentException("The prompt name must not be empty.", nameof(name));
        }

        if (options is { Version: not null, Label: not null })
        {
            throw new ArgumentException("Pass either Version or Label, not both.", nameof(options));
        }

        var key = new PromptCacheKey(name, options?.Version, options?.Label);
        var ttl = options?.CacheTtl ?? _options.PromptCacheTtl;

        if (_cache.TryGetValue(key, out var cached))
        {
            if (IsFresh(cached, ttl))
            {
                return cached.ToResult(PromptSource.Cache);
            }

            // A failed refetch put this entry in cooldown. Serving it keeps a throttled client from
            // sending another request on every call. A zero TTL asks for a fetch on every call, so it opts out.
            if (ttl > TimeSpan.Zero && cached.RetryNotBefore is { } retryNotBefore && _timeProvider.GetUtcNow() < retryNotBefore)
            {
                return cached.ToResult(PromptSource.StaleCache);
            }
        }

        try
        {
            var fetched = await FetchOnceAsync(key, ttl, cancellationToken).ConfigureAwait(false);
            return fetched.ToResult(PromptSource.Api);
        }
        catch (PromptFetchException e)
        {
            if (cached is not null)
            {
                StartCooldown(key, cached, e);
                _logger.LogWarningServingStaleCache(e, Describe(key));
                return cached.ToResult(PromptSource.StaleCache);
            }

            if (options?.Fallback is { } fallback)
            {
                _logger.LogWarningServingFallback(e, Describe(key));
                return new PromptResult { Source = PromptSource.CodeFallback, Prompt = fallback };
            }

            throw;
        }
    }

    bool IsFresh(CachedPrompt cached, TimeSpan ttl)
        => ttl > TimeSpan.Zero && _timeProvider.GetUtcNow() - cached.FetchedAt < ttl;

    void StartCooldown(PromptCacheKey key, CachedPrompt cached, PromptFetchException error)
    {
        var cooldown = error.RetryAfter ?? DefaultRefetchCooldown;
        if (cooldown > MaxRefetchCooldown)
        {
            cooldown = MaxRefetchCooldown;
        }

        var retryNotBefore = _timeProvider.GetUtcNow() + cooldown;
        if (cached.RetryNotBefore is { } existing && existing >= retryNotBefore)
        {
            return;
        }

        // A fetch that succeeded in the meantime replaced the entry, and that entry must stay as it is.
        _cache.TryUpdate(key, cached.WithRetryNotBefore(retryNotBefore), cached);
    }

    /// <summary>
    /// Fetches a prompt, sharing one request with every other caller that wants the same key at the same time.
    /// </summary>
    async Task<CachedPrompt> FetchOnceAsync(PromptCacheKey key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        // Another caller may have finished a fetch since this caller's cache check.
        if (_cache.TryGetValue(key, out var cached) && IsFresh(cached, ttl))
        {
            return cached;
        }

        var pending = _inFlight.GetOrAdd(key, k => new Lazy<Task<CachedPrompt>>(() => FetchAndCacheAsync(k)));
        return await pending.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task<CachedPrompt> FetchAndCacheAsync(PromptCacheKey key)
    {
        try
        {
            // The request is shared by every caller waiting on this key, so one caller's cancellation
            // must not cancel it. Callers stop waiting through their own token instead.
            var fetched = await FetchAsync(key, CancellationToken.None).ConfigureAwait(false);
            // Cached before the in-flight entry is released, so a caller that arrives in between
            // finds the entry instead of starting a request.
            _cache[key] = fetched;
            return fetched;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    async Task<CachedPrompt> FetchAsync(PromptCacheKey key, CancellationToken cancellationToken)
    {
        if (_isDisabled)
        {
            throw Error(key, "client_disabled", "The PostHog client is disabled, so no prompt was fetched.");
        }

        var secretKey = _options.SecretKey
            ?? throw Error(
                key,
                "missing_secret_key",
                "Fetching prompts needs SecretKey set to a personal API key (phx_...) with the llm_prompt:read scope.");

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(key));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw Error(key, "network_error", $"Fetching {Describe(key)} failed: {e.Message}", innerException: e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw Error(key, "network_error", $"Fetching {Describe(key)} timed out.", innerException: e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw ErrorFromResponse(key, response);
            }

            return await ReadPromptAsync(key, response, cancellationToken).ConfigureAwait(false);
        }
    }

    Uri BuildUrl(PromptCacheKey key)
    {
        var query = $"token={Uri.EscapeDataString(ProjectToken)}";
        if (key.Version is { } version)
        {
            query += $"&version={version}";
        }
        if (key.Label is { } label)
        {
            query += $"&label={Uri.EscapeDataString(label)}";
        }

        return new Uri(_options.HostUrl, $"{PromptsPath}{Uri.EscapeDataString(key.Name)}/?{query}");
    }

    string ProjectToken => _options.ProjectToken
        ?? throw new InvalidOperationException("Either ProjectToken or ProjectApiKey must be provided.");

    PromptFetchException ErrorFromResponse(PromptCacheKey key, HttpResponseMessage response)
    {
        var reference = Describe(key);
        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => Error(key, "not_found", $"{Capitalize(reference)} was not found.", response.StatusCode),
            HttpStatusCode.Unauthorized => Error(
                key,
                "unauthorized",
                $"Authentication failed for {reference}. SecretKey must be a personal API key (phx_...); "
                + "a project secret key (phs_...) is not accepted for prompts.",
                response.StatusCode),
            HttpStatusCode.Forbidden => Error(
                key,
                "forbidden",
                $"Access denied for {reference}. Check that SecretKey has the llm_prompt:read scope.",
                response.StatusCode),
            (HttpStatusCode)429 => Error(
                key,
                "rate_limited",
                $"Fetching {reference} was rate limited.",
                response.StatusCode,
                ParseRetryAfter(response.Headers.RetryAfter)),
            _ => Error(key, "http_error", $"Fetching {reference} failed: HTTP {(int)response.StatusCode}.", response.StatusCode),
        };
    }

    TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : null;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - _timeProvider.GetUtcNow();
            return delay > TimeSpan.Zero ? delay : null;
        }

        return null;
    }

    async Task<CachedPrompt> ReadPromptAsync(
        PromptCacheKey key,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
#if NET8_0_OR_GREATER
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException e)
        {
            throw InvalidResponse(key, e);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("prompt", out var prompt) || prompt.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionNumber))
            {
                throw InvalidResponse(key);
            }

            string? label = null;
            if (root.TryGetProperty("label", out var labelElement) && labelElement.ValueKind == JsonValueKind.String)
            {
                label = labelElement.GetString();
            }

            // An older PostHog server ignores the label parameter and returns the latest version without a label.
            // The other SDKs warn and still serve that version, so this one does the same.
            if (key.Label is { } requestedLabel && label != requestedLabel)
            {
                _logger.LogWarningLabelMismatch(requestedLabel, key.Name, label);
            }

            // Older servers do not send config; a non-object value is treated as absent, like the other SDKs.
            JsonElement? config = null;
            if (root.TryGetProperty("config", out var configElement) && configElement.ValueKind == JsonValueKind.Object)
            {
                config = configElement.Clone();
            }

            return new CachedPrompt(
                prompt.GetString()!,
                name.GetString()!,
                versionNumber,
                label,
                config,
                _timeProvider.GetUtcNow());
        }
    }

    static PromptFetchException InvalidResponse(PromptCacheKey key, Exception? innerException = null)
        => Error(key, "invalid_response", $"PostHog returned an invalid response for {Describe(key)}.", innerException: innerException);

    static PromptFetchException Error(
        PromptCacheKey key,
        string code,
        string message,
        HttpStatusCode? statusCode = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        => new(code, message, statusCode, retryAfter, key.Name, key.Version, key.Label, innerException);

    static string Describe(PromptCacheKey key)
    {
        var reference = $"prompt \"{key.Name}\"";
        if (key.Version is { } version)
        {
            return $"{reference} version {version}";
        }

        if (key.Label is { } label)
        {
            return $"{reference} label \"{label}\"";
        }

        return reference;
    }

    static string Capitalize(string value) => char.ToUpperInvariant(value[0]) + value.Substring(1);

    readonly record struct PromptCacheKey(string Name, int? Version, string? Label);

    sealed class CachedPrompt(
        string prompt,
        string name,
        int version,
        string? label,
        JsonElement? config,
        DateTimeOffset fetchedAt,
        DateTimeOffset? retryNotBefore = null)
    {
        public DateTimeOffset FetchedAt { get; } = fetchedAt;

        /// <summary>
        /// Until when a failed refetch holds this entry in cooldown, or <c>null</c> when it is not in cooldown.
        /// </summary>
        public DateTimeOffset? RetryNotBefore { get; } = retryNotBefore;

        public CachedPrompt WithRetryNotBefore(DateTimeOffset value)
            => new(prompt, name, version, label, config, FetchedAt, value);

        public PromptResult ToResult(PromptSource source) => new()
        {
            Source = source,
            Prompt = prompt,
            Name = name,
            Version = version,
            Label = label,
            Config = config,
        };
    }
}

internal static partial class PromptFetcherLoggerExtensions
{
    [LoggerMessage(
        EventId = 1300,
        Level = LogLevel.Warning,
        Message = "[PROMPTS] Failed to fetch {Prompt}, serving the stale cached version")]
    public static partial void LogWarningServingStaleCache(this ILogger<PromptFetcher> logger, Exception exception, string prompt);

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Warning,
        Message = "[PROMPTS] Failed to fetch {Prompt}, serving the fallback")]
    public static partial void LogWarningServingFallback(this ILogger<PromptFetcher> logger, Exception exception, string prompt);

    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Warning,
        Message = "[PROMPTS] Requested label \"{RequestedLabel}\" for prompt \"{Name}\" but the server resolved label \"{ResolvedLabel}\". It may not support prompt labels yet and returned the latest version instead")]
    public static partial void LogWarningLabelMismatch(this ILogger<PromptFetcher> logger, string requestedLabel, string name, string? resolvedLabel);
}
