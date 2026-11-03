using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PostHog.Library;
using static PostHog.Library.Ensure;

namespace PostHog.Prompts;

/// <summary>
/// Fetches prompts from the PostHog prompts API and caches them per name, version and label.
/// </summary>
internal sealed class PromptFetcher : IPostHogPrompts
{
    const string PromptsPath = "/api/environments/@current/llm_prompts/name/";

    readonly HttpClient _httpClient;
    readonly PostHogOptions _options;
    readonly bool _isDisabled;
    readonly TimeProvider _timeProvider;
    readonly ILogger<PromptFetcher> _logger;
    readonly ConcurrentDictionary<PromptCacheKey, CachedPrompt> _cache = new();

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

        if (_cache.TryGetValue(key, out var cached) && IsFresh(cached, ttl))
        {
            return cached.ToResult(PromptSource.Cache);
        }

        try
        {
            var fetched = await FetchAsync(key, cancellationToken).ConfigureAwait(false);
            _cache[key] = fetched;
            return fetched.ToResult(PromptSource.Api);
        }
        catch (PromptFetchException e)
        {
            if (cached is not null)
            {
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
        DateTimeOffset fetchedAt)
    {
        public DateTimeOffset FetchedAt { get; } = fetchedAt;

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
}
