using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PostHog.Json;
using static PostHog.Library.Ensure;

namespace PostHog.Emails;

/// <summary>
/// Sends one transactional email per call to the messaging send API on the app host, and reads the status of a sent
/// email. Owns the attempt loop, so every attempt of a send carries the same idempotency key.
/// </summary>
internal sealed class EmailSender : IPostHogEmails
{
    const string SendPath = "/api/projects/@current/messaging/send/email/";
    const string StatusPath = "/api/projects/@current/messaging/emails/";
    const int MaxTemplateKeyLength = 400;
    const int MaxIdempotencyKeyLength = 255;
    static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);
    const int DefaultMaxRetries = 2;
    const string UsAppHost = "https://us.posthog.com";
    const string EuAppHost = "https://eu.posthog.com";

    static readonly Dictionary<string, string> CloudAppHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["https://us.i.posthog.com"] = UsAppHost,
        ["https://us.posthog.com"] = UsAppHost,
        ["https://app.posthog.com"] = UsAppHost,
        ["https://eu.i.posthog.com"] = EuAppHost,
        ["https://eu.posthog.com"] = EuAppHost,
    };

    readonly HttpClient? _httpClient;
    readonly PostHogOptions _options;
    readonly bool _isDisabled;
    readonly TimeProvider _timeProvider;
    readonly ILogger _logger;
    readonly object _gate = new();
    int _inFlight;
    bool _closed;
    TaskCompletionSource<bool>? _drained;

    public EmailSender(
        HttpClient? httpClient,
        PostHogOptions options,
        bool isDisabled,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _httpClient = httpClient;
        _options = options;
        _isDisabled = isDisabled || httpClient is null;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SendEmailResult> SendAsync(SendEmailOptions email, CancellationToken cancellationToken = default)
    {
        var prepared = PrepareSend(email, MaxRetriesOption);
        using var _ = Enter("no email was sent");
        var (appHost, secretKey) = Connect(
            "no email was sent",
            "Sending email needs SecretKey, a project secret API key with the transactional_email:write scope.");
        var url = new Uri(appHost + SendPath);
        var result = await WithRetriesAsync(
            prepared.MaxRetries,
            async () => ParseSendReply(
                await RequestAsync(HttpMethod.Post, url, secretKey, prepared.Body, prepared.IdempotencyKey, cancellationToken)
                    .ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);
        foreach (var warning in result.Warnings)
        {
            _logger.LogWarningEmailWarning(warning.Code, warning.Message);
        }

        return result;
    }

    public async Task<EmailStatus> GetAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(NotNull(messageId), "D", out var id))
        {
            throw new ArgumentException("messageId must be the message ID that SendAsync returned.", nameof(messageId));
        }

        using var _ = Enter("no email status was read");
        var (appHost, secretKey) = Connect(
            "no email status was read",
            "Reading an email status needs SecretKey, a project secret API key with the transactional_email:read or transactional_email:write scope.");
        var url = new Uri(appHost + StatusPath + id.ToString("D") + "/");
        return await WithRetriesAsync(
            MaxRetriesOption,
            async () => ParseStatusReply(
                await RequestAsync(HttpMethod.Get, url, secretKey, body: null, idempotencyKey: null, cancellationToken)
                    .ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops new email requests, which then fail with <c>client_closed</c>, and waits until every send and status
    /// read in flight, retries included, has settled.
    /// </summary>
    public Task CloseAsync()
    {
        lock (_gate)
        {
            _closed = true;
            if (_inFlight == 0)
            {
                return Task.CompletedTask;
            }

            _drained ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _drained.Task;
        }
    }

    int MaxRetriesOption => _options.EmailRequestMaxRetries >= 0 ? _options.EmailRequestMaxRetries : DefaultMaxRetries;

    TimeSpan RequestTimeout => _options.EmailRequestTimeout > TimeSpan.Zero
        ? _options.EmailRequestTimeout
        : DefaultRequestTimeout;

    InFlight Enter(string nothingDone)
    {
        lock (_gate)
        {
            if (_isDisabled)
            {
                throw new EmailException("client_disabled", $"The PostHog client is disabled, so {nothingDone}.");
            }

            if (_closed)
            {
                throw new EmailException("client_closed", $"The PostHog client is shut down, so {nothingDone}.");
            }

            _inFlight++;
            return new InFlight(this);
        }
    }

    void Leave()
    {
        lock (_gate)
        {
            _inFlight--;
            if (_inFlight == 0)
            {
                _drained?.TrySetResult(true);
            }
        }
    }

    (string AppHost, string SecretKey) Connect(string nothingDone, string missingSecretKey)
    {
        var appHost = ResolveAppHost(_options.HostUrl, _options.AppHost)
                      ?? throw new EmailException("missing_app_host", MissingAppHostMessage(nothingDone));
        if (!_options.AllowInsecureAppHost && appHost.StartsWith("http:", StringComparison.OrdinalIgnoreCase))
        {
            throw new EmailException(
                "insecure_app_host",
                $"AppHost {appHost} uses http, which would send SecretKey unencrypted, so {nothingDone}. "
                + "Use an https AppHost, or set AllowInsecureAppHost for local development.");
        }

        var secretKey = _options.SecretKey?.Trim();
        if (string.IsNullOrEmpty(secretKey))
        {
            throw new EmailException("missing_secret_key", missingSecretKey);
        }

        if (_options.MigratedFrom is { } migratedFrom && !IsVisibleAscii(migratedFrom, int.MaxValue))
        {
            throw new EmailException(
                "invalid_migrated_from",
                $"MigratedFrom must be visible ASCII characters, such as \"resend\", so {nothingDone}.");
        }

        return (appHost, secretKey!);
    }

    string MissingAppHostMessage(string nothingDone) => _options.AppHost is null
        ? "The client's HostUrl is not PostHog Cloud, so the client can't tell where to send email "
          + $"and {nothingDone}. Set AppHost to your PostHog app host in PostHogOptions."
        : $"AppHost must be an absolute http or https URL, such as https://posthog.example.com, so {nothingDone}.";

    /// <summary>
    /// The app host for an email request, or <c>null</c> when the client can't tell it. PostHog Cloud ingestion hosts
    /// don't serve the app API, so they map to their region's app host.
    /// </summary>
    internal static string? ResolveAppHost(Uri? hostUrl, Uri? appHost)
    {
        if (appHost is not null)
        {
            if (!appHost.IsAbsoluteUri || (appHost.Scheme != Uri.UriSchemeHttp && appHost.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            var explicitHost = TrimTrailingSlash(appHost);
            return CloudAppHosts.TryGetValue(explicitHost, out var mapped) ? mapped : explicitHost;
        }

        return hostUrl is { IsAbsoluteUri: true } && CloudAppHosts.TryGetValue(TrimTrailingSlash(hostUrl), out var cloud)
            ? cloud
            : null;
    }

    static string TrimTrailingSlash(Uri uri) => uri.GetLeftPart(UriPartial.Path).TrimEnd('/');

    async Task<T> WithRetriesAsync<T>(int maxRetries, Func<Task<T>> attemptOnce, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            EmailException error;
            try
            {
                return await attemptOnce().ConfigureAwait(false);
            }
            catch (EmailException e)
            {
                error = e;
            }

            var delay = RetryDelay(error, attempt, maxRetries);
            if (delay is null)
            {
                throw error;
            }

            await DelayAsync(delay.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    static TimeSpan? RetryDelay(EmailException error, int attempt, int maxRetries)
    {
        if (attempt >= maxRetries || !IsRetryable(error))
        {
            return null;
        }

        if (error.RetryAfter > MaxRetryDelay)
        {
            return null;
        }

        var backoffMs = Math.Min(
            MaxRetryDelay.TotalMilliseconds,
            1000 * Math.Pow(2, attempt) * (0.8 + (SharedJitter.Next() * 0.4)));
        var backoff = TimeSpan.FromMilliseconds(backoffMs);
        return error.RetryAfter > backoff ? error.RetryAfter : backoff;
    }

    static bool IsRetryable(EmailException error)
    {
        return error.StatusCode switch
        {
            null => error.Code == "network_error",
            HttpStatusCode.Conflict => error.Code == "idempotency_key_in_flight",
            (HttpStatusCode)429 => error.Code != "quota_limited",
            HttpStatusCode.ServiceUnavailable => true,
            _ => false,
        };
    }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        return Task.Delay(delay, _timeProvider, cancellationToken);
#else
        return _timeProvider.Delay(delay, cancellationToken);
#endif
    }

    async Task<Reply> RequestAsync(
        HttpMethod method,
        Uri url,
        string secretKey,
        string? body,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        if (_options.MigratedFrom is { } migratedFrom)
        {
            request.Headers.TryAddWithoutValidation("X-PostHog-Migrated-From", migratedFrom);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient!.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw NetworkError($"The request to PostHog timed out after {RequestTimeout.TotalSeconds:0.###}s.", e);
        }
        catch (HttpRequestException e)
        {
            throw NetworkError("The request to PostHog failed.", e);
        }
        catch (IOException e)
        {
            throw NetworkError("The request to PostHog failed.", e);
        }

        using (response)
        {
#if NET5_0_OR_GREATER
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

            return new Reply(response.StatusCode, ParseObject(text), ReadRetryAfter(response));
        }
    }

    static EmailException NetworkError(string message, Exception innerException)
        => new("network_error", message, innerException: innerException);

    TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        TimeSpan? wait = retryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - _timeProvider.GetUtcNow(),
            _ => null,
        };
        return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
    }

    static JsonElement? ParseObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static SendEmailResult ParseSendReply(Reply reply)
    {
        if (!IsSuccess(reply.Status))
        {
            throw ReplyError(reply);
        }

        var id = Text(reply.Fields, "message_id")
                 ?? throw new EmailException(
                     "api_error",
                     "PostHog answered without a message ID.",
                     reply.Status);
        var warnings = new List<EmailWarning>();
        if (reply.Fields is { } fields
            && fields.TryGetProperty("warnings", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var warning in list.EnumerateArray())
            {
                if (warning.ValueKind == JsonValueKind.Object)
                {
                    warnings.Add(new EmailWarning
                    {
                        Code = Text(warning, "code") ?? "warning",
                        Message = Text(warning, "detail") ?? Text(warning, "message") ?? string.Empty,
                        Fix = Text(warning, "fix"),
                    });
                }
            }
        }

        var replayed = reply.Fields is { } replyFields
                       && replyFields.TryGetProperty("replayed", out var replayedValue)
                       && replayedValue.ValueKind == JsonValueKind.True;
        return new SendEmailResult { Id = id, Replayed = replayed, Warnings = warnings };
    }

    static EmailStatus ParseStatusReply(Reply reply)
    {
        if (!IsSuccess(reply.Status))
        {
            throw ReplyError(reply);
        }

        var id = Text(reply.Fields, "id");
        var status = Text(reply.Fields, "status");
        var createdAt = Time(reply.Fields, "created_at");
        var statusAt = Time(reply.Fields, "status_at");
        if (id is null || status is null || createdAt is null || statusAt is null)
        {
            throw new EmailException("api_error", "PostHog answered without an email status.", reply.Status);
        }

        return new EmailStatus
        {
            Id = id,
            Status = status,
            CreatedAt = createdAt.Value,
            StatusAt = statusAt.Value,
            Reason = Text(reply.Fields, "reason"),
        };
    }

    static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    static EmailException ReplyError(Reply reply)
    {
        var docsUrl = Text(reply.Fields, "docs_url") is { } docs && Uri.TryCreate(docs, UriKind.Absolute, out var uri)
            ? uri
            : null;
        return new EmailException(
            Text(reply.Fields, "code") ?? "api_error",
            Text(reply.Fields, "detail") ?? $"PostHog answered HTTP {(int)reply.Status}.",
            reply.Status,
            why: Text(reply.Fields, "why"),
            fix: Text(reply.Fields, "fix"),
            docsUrl: docsUrl,
            field: Text(reply.Fields, "attr"),
            retryAfter: reply.RetryAfter);
    }

    static string? Text(JsonElement? element, string name)
        => element is { ValueKind: JsonValueKind.Object } value
           && value.TryGetProperty(name, out var property)
           && property.ValueKind == JsonValueKind.String
           && property.GetString() is { Length: > 0 } text
            ? text
            : null;

    static DateTimeOffset? Time(JsonElement? element, string name)
        => Text(element, name) is { } text
           && DateTimeOffset.TryParse(
               text,
               System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.RoundtripKind,
               out var time)
            ? time
            : null;

    static PreparedSend PrepareSend(SendEmailOptions email, int defaultMaxRetries)
    {
        NotNull(email);
        if (string.IsNullOrWhiteSpace(email.To))
        {
            throw new ArgumentException("To must be one email address.", nameof(email));
        }

        if (email.DistinctId is { Length: 0 })
        {
            throw new ArgumentException("DistinctId must not be empty. Leave it null to leave it out.", nameof(email));
        }

        if (email.IdempotencyKey is { } key && !IsVisibleAscii(key, MaxIdempotencyKeyLength))
        {
            throw new ArgumentException(
                "IdempotencyKey must be 1 to 255 visible ASCII characters without spaces.",
                nameof(email));
        }

        if (email.MaxRetries < 0)
        {
            throw new ArgumentException("MaxRetries must not be negative.", nameof(email));
        }

        var body = email switch
        {
            SendTemplateEmailOptions template => TemplateBody(template),
            SendCodeEmailOptions code => CodeBody(code),
            _ => throw new ArgumentException(
                "Send a SendTemplateEmailOptions or a SendCodeEmailOptions.",
                nameof(email)),
        };
        return new PreparedSend(
            JsonSerializer.Serialize(body, JsonSerializerHelper.Options),
            email.IdempotencyKey ?? Guid.NewGuid().ToString("D"),
            email.MaxRetries ?? defaultMaxRetries);
    }

    static Dictionary<string, object?> TemplateBody(SendTemplateEmailOptions email)
    {
        if (email.TemplateId == Guid.Empty)
        {
            throw new ArgumentException("TemplateId must be the ID of a template made in PostHog.", nameof(email));
        }

        var body = new Dictionary<string, object?>
        {
            ["template_id"] = email.TemplateId.ToString("D"),
            ["to"] = email.To,
            ["message_data"] = email.TemplateData ?? new Dictionary<string, object>(),
        };
        if (email.DistinctId is not null)
        {
            body["distinct_id"] = email.DistinctId;
        }

        return body;
    }

    static Dictionary<string, object?> CodeBody(SendCodeEmailOptions email)
    {
        if (!IsTemplateKey(email.TemplateKey))
        {
            throw new ArgumentException(
                "TemplateKey can only have letters, digits, hyphens (-) and underscores (_), at most 400.",
                nameof(email));
        }

        if (email.Subject is null)
        {
            throw new ArgumentException("A send with TemplateKey needs a Subject.", nameof(email));
        }

        if (email.Html is null && email.Text is null)
        {
            throw new ArgumentException("A send with TemplateKey needs Html, Text or both.", nameof(email));
        }

        if (email.ReplyTo is { } replyTo && replyTo.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("ReplyTo must not hold a blank address.", nameof(email));
        }

        var body = new Dictionary<string, object?>
        {
            ["template_key"] = email.TemplateKey,
            ["to"] = email.To,
            ["subject"] = email.Subject,
        };
        if (email.Html is not null)
        {
            body["html"] = email.Html;
        }

        if (email.Text is not null)
        {
            body["text"] = email.Text;
        }

        if (email.From is not null)
        {
            body["from"] = email.From;
        }

        if (email.ReplyTo is not null)
        {
            body["reply_to"] = email.ReplyTo;
        }

        body["capture_engagement"] = email.CaptureEngagement;
        if (email.DistinctId is not null)
        {
            body["distinct_id"] = email.DistinctId;
        }

        return body;
    }

    static bool IsTemplateKey(string? key)
    {
        if (key is not { Length: > 0 and <= MaxTemplateKeyLength })
        {
            return false;
        }

        foreach (var c in key)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    static bool IsVisibleAscii(string value, int maxLength)
    {
        if (value.Length == 0 || value.Length > maxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is < '\x21' or > '\x7e')
            {
                return false;
            }
        }

        return true;
    }

    readonly record struct PreparedSend(string Body, string IdempotencyKey, int MaxRetries);

    readonly record struct Reply(HttpStatusCode Status, JsonElement? Fields, TimeSpan? RetryAfter);

    readonly struct InFlight(EmailSender sender) : IDisposable
    {
        public void Dispose() => sender.Leave();
    }

    static class SharedJitter
    {
        static readonly object Gate = new();
#pragma warning disable CA5394 // Retry jitter is not security sensitive.
        static readonly Random Random = new();

        public static double Next()
        {
            lock (Gate)
            {
                return Random.NextDouble();
            }
        }
#pragma warning restore CA5394
    }
}

internal static partial class EmailSenderLoggerExtensions
{
    [LoggerMessage(
        EventId = 600,
        Level = LogLevel.Warning,
        Message = "PostHog email warning {Code}: {Message}")]
    public static partial void LogWarningEmailWarning(this ILogger logger, string code, string message);
}
