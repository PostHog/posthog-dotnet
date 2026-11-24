using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using PostHog.Json;
using static PostHog.Library.Ensure;

namespace PostHog.Emails;

/// <summary>
/// Sends transactional email to the messaging send API on the app host, one email or a batch per call, and reads the
/// status of a sent email. Owns the attempt loop, so every attempt of a send carries the same idempotency keys.
/// </summary>
internal sealed partial class EmailSender : IPostHogEmails
{
    const string SendPath = "/api/messaging/send/email/";
    const string StatusPath = "/api/messaging/emails/";
    const string UploadsPath = "/api/messaging/attachments/uploads/";
    const int MaxTemplateKeyLength = 400;
    const int MaxIdempotencyKeyLength = 255;
    static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);
    const int DefaultMaxRetries = 2;
    const string UsAppHost = "https://us.posthog.com";
    const string SendNeedsSecretKey =
        "Sending email needs SecretKey, a project secret API key with the transactional_email:write scope.";

    const string EuAppHost = "https://eu.posthog.com";
    const string ScheduledAtField = "scheduled_at";
    static readonly TimeSpan MaxClockDrift = TimeSpan.FromSeconds(60);
    static readonly string[] AnsweredTimeFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.fK",
        "yyyy-MM-dd'T'HH:mm:ss.ffK",
        "yyyy-MM-dd'T'HH:mm:ss.fffK",
        "yyyy-MM-dd'T'HH:mm:ss.ffffK",
        "yyyy-MM-dd'T'HH:mm:ss.fffffK",
        "yyyy-MM-dd'T'HH:mm:ss.ffffffK",
        "yyyy-MM-dd'T'HH:mm:ss.fffffffK",
    ];

    /// <summary>
    /// Makes the client that uploads files, whatever <see cref="IHttpClientFactory"/> the app supplied. Over the
    /// default transport it never follows a redirect, so a file goes only to the URL PostHog signed, and none of the
    /// app's handlers or default headers reach the file storage. It has no timeout of its own: each upload runs until
    /// the app's PostHog <see cref="HttpClient.Timeout"/>. Its owner disposes it after the sends drain.
    /// </summary>
    /// <param name="transport">Stands in for the file storage in tests; null gives the default transport.</param>
#pragma warning disable CA2000 // The client owns the handler it makes and disposes it with itself.
    internal static HttpClient CreateUploadClient(HttpMessageHandler? transport) =>
        new(transport ?? NoRedirectHandler(), disposeHandler: transport is null) { Timeout = Timeout.InfiniteTimeSpan };
#pragma warning restore CA2000

    static HttpClientHandler NoRedirectHandler() => new HttpClientHandler { AllowAutoRedirect = false };

    static readonly Dictionary<string, string> CloudAppHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["https://us.i.posthog.com"] = UsAppHost,
        ["https://us.posthog.com"] = UsAppHost,
        ["https://app.posthog.com"] = UsAppHost,
        ["https://eu.i.posthog.com"] = EuAppHost,
        ["https://eu.posthog.com"] = EuAppHost,
    };

    readonly HttpClient? _httpClient;
    readonly HttpClient _uploadClient;
    readonly PostHogOptions _options;
    readonly bool _isDisabled;
    readonly TimeProvider _timeProvider;
    readonly ILogger _logger;
    readonly object _gate = new();
    int _inFlight;
    bool _closed;
    TaskCompletionSource<bool>? _drained;

    /// <param name="httpClient">The app's PostHog client, which talks to PostHog.</param>
    /// <param name="uploadClient">Carries uploads to the file storage: one from <see cref="CreateUploadClient"/>.</param>
    /// <param name="options">The PostHog options.</param>
    /// <param name="isDisabled">Whether the client is disabled.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public EmailSender(
        HttpClient? httpClient,
        HttpClient uploadClient,
        PostHogOptions options,
        bool isDisabled,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _httpClient = httpClient;
        _uploadClient = uploadClient;
        _options = options;
        _isDisabled = isDisabled || httpClient is null;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SendEmailResult> SendAsync(SendEmailOptions email, CancellationToken cancellationToken = default)
    {
        var prepared = PrepareSend(email, MaxRetriesOption);
        using var _ = Enter("no email was sent");
        var (appHost, secretKey) = Connect("no email was sent", SendNeedsSecretKey);
        if (prepared.ScheduledAt is not null)
        {
            RefuseToScheduleOffCloud(appHost);
        }

        await UploadAsync(prepared.Attachments, appHost, secretKey, prepared.MaxRetries, cancellationToken)
            .ConfigureAwait(false);
        var url = new Uri(appHost + SendPath);
        var result = await WithRetriesAsync(
            prepared.MaxRetries,
            async () => ParseSendReply(
                await RequestAsync(HttpMethod.Post, url, secretKey, prepared.Body, prepared.IdempotencyKey, cancellationToken)
                    .ConfigureAwait(false),
                prepared.ScheduledAt),
            cancellationToken).ConfigureAwait(false);
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

    async Task UploadAsync(
        HashedAttachments attachments,
        string appHost,
        string secretKey,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        var files = attachments.Distinct;
        if (files.Count == 0)
        {
            return;
        }

        var url = new Uri(appHost + UploadsPath);
        var body = JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                ["files"] = files
                    .Select(file => new Dictionary<string, object> { ["sha256"] = file.Sha256, ["size"] = file.Size })
                    .ToList(),
            },
            JsonSerializerHelper.Options);
        var uploads = await WithRetriesAsync(
            maxRetries,
            async () => ParseUploadsReply(
                await RequestAsync(HttpMethod.Post, url, secretKey, body, idempotencyKey: null, cancellationToken)
                    .ConfigureAwait(false),
                files),
            cancellationToken).ConfigureAwait(false);
        foreach (var (file, target) in uploads)
        {
            if (!_options.AllowInsecureAppHost && target.Url.Scheme != Uri.UriSchemeHttps)
            {
                throw new EmailException(
                    "insecure_app_host",
                    $"PostHog answered with an http upload URL for {file.File.Filename}, so no email was sent.",
                    why: "Over http the file would travel unencrypted.",
                    fix: "Serve PostHog's file storage over https, or set AllowInsecureAppHost for local development.",
                    field: $"attachments__{file.Index}");
            }
        }

        foreach (var (file, target) in uploads)
        {
            await WithRetriesAsync(
                maxRetries,
                () => PutAsync(file, target, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
    }

    async Task<bool> PutAsync(HashedAttachment file, UploadTarget target, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(target.Method, target.Url) { Content = UploadContent(file) };
        foreach (var header in target.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                request.Content.Headers.Remove(header.Key);
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        HttpResponseMessage response;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_httpClient!.Timeout);
        try
        {
            response = await _uploadClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw NetworkError($"Uploading {file.File.Filename} timed out, so no email was sent.", e);
        }
        catch (HttpRequestException e)
        {
            throw NetworkError($"Uploading {file.File.Filename} failed, so no email was sent.", e);
        }
        catch (IOException e)
        {
            throw NetworkError($"Uploading {file.File.Filename} failed, so no email was sent.", e);
        }

        using (response)
        {
            if (IsSuccess(response.StatusCode))
            {
                return true;
            }

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                throw new EmailException(
                    "attachment_upload_failed",
                    $"Uploading {file.File.Filename} failed, so no email was sent.",
                    response.StatusCode,
                    why: $"The file storage answered HTTP {(int)response.StatusCode}, a redirect, and an upload "
                         + "never follows one, so the file goes only where PostHog signed it to.",
                    fix: "Send the email again. If it keeps failing, check that nothing between your app and the "
                         + "file storage, such as a proxy, redirects the request.",
                    field: $"attachments__{file.Index}");
            }

#if NET5_0_OR_GREATER
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
            throw new EmailException(
                "attachment_upload_failed",
                $"Uploading {file.File.Filename} failed, so no email was sent.",
                response.StatusCode,
                why: $"The file storage answered {StorageAnswer(response.StatusCode, text)}",
                fix: "Send the email again. If the upload keeps failing, check that nothing between your app and "
                     + "the file storage, such as a proxy, changes the request.",
                field: $"attachments__{file.Index}",
                retryAfter: ReadRetryAfter(response));
        }
    }

    static ByteArrayContent UploadContent(HashedAttachment file)
        => MemoryMarshal.TryGetArray(file.File.Content, out var segment)
            ? new ByteArrayContent(segment.Array!, segment.Offset, segment.Count)
            : new ByteArrayContent(file.File.Content.ToArray());

    static string StorageAnswer(HttpStatusCode status, string text)
    {
        const int maxAnswerLength = 500;
        var answer = $"HTTP {(int)status}";
        if (StorageError(text) is { } error)
        {
            return $"{answer} {error.Code}: {error.Message}";
        }

        var trimmed = text.Trim();
        return trimmed.Length switch
        {
            0 => answer + ".",
            > maxAnswerLength => $"{answer}: {trimmed.Substring(0, maxAnswerLength)}",
            _ => $"{answer}: {trimmed}",
        };
    }

    static (string Code, string Message)? StorageError(string text)
    {
        try
        {
            using var reader = XmlReader.Create(
                new StringReader(text),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var error = XDocument.Load(reader).Root;
            return error?.Name.LocalName == "Error" && error.Element("Code")?.Value is { Length: > 0 } code
                ? (code, error.Element("Message")?.Value ?? string.Empty)
                : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    static EmailException NoUploadRouteOrNoCodeSends(Reply reply)
        => ReplyError(reply, defaultCode: "not_found", defaultField: "attachments");

    static List<(HashedAttachment File, UploadTarget Target)> ParseUploadsReply(
        Reply reply,
        IReadOnlyList<HashedAttachment> files)
    {
        if (reply.Status == HttpStatusCode.NotFound)
        {
            throw NoUploadRouteOrNoCodeSends(reply);
        }

        if (!IsSuccess(reply.Status))
        {
            throw ReplyError(reply);
        }

        var answers = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (reply.Fields is { } fields
            && fields.TryGetProperty("files", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var answer in list.EnumerateArray())
            {
                if (Text(answer, "sha256") is { } sha256)
                {
                    answers[sha256] = answer;
                }
            }
        }

        var uploads = new List<(HashedAttachment, UploadTarget)>();
        foreach (var file in files)
        {
            answers.TryGetValue(file.Sha256, out var answer);
            switch (Text(answer, "status"))
            {
                case "present":
                    continue;
                case "upload" when ReadUploadTarget(answer) is { } target:
                    uploads.Add((file, target));
                    continue;
                default:
                    throw new EmailException(
                        "api_error",
                        $"PostHog answered without where to upload {file.File.Filename}, so no email was sent.",
                        reply.Status,
                        why: $"PostHog's answer about the file with SHA-256 {file.Sha256} is missing or has no "
                             + "usable upload URL, method and headers.",
                        fix: "Send the email again. If it keeps failing, check that your PostHog version supports "
                             + "attachment uploads.",
                        field: $"attachments__{file.Index}");
            }
        }

        return uploads;
    }

    static UploadTarget? ReadUploadTarget(JsonElement answer)
    {
        if (!Uri.TryCreate(Text(answer, "url"), UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            || Text(answer, "method") is not { } method
            || !answer.TryGetProperty("headers", out var headerFields)
            || headerFields.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headerFields.EnumerateObject())
        {
            if (header.Value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            headers[header.Name] = header.Value.GetString()!;
        }

        try
        {
            return new UploadTarget(url, new HttpMethod(method), headers);
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return null;
        }
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

    static void RefuseToScheduleOffCloud(string appHost)
    {
        if (appHost is UsAppHost or EuAppHost)
        {
            return;
        }

        throw new EmailException(
            "scheduling_requires_cloud",
            $"Only PostHog Cloud ({UsAppHost} or {EuAppHost}) schedules emails, and this client sends to "
            + $"{new Uri(appHost).GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped)}, "
            + "so no email was sent.",
            why: "A PostHog that doesn't schedule emails, such as a self-hosted one, or a proxy in front of PostHog "
                 + "Cloud, could send the email at once instead of at ScheduledAt.",
            fix: "Send through PostHog Cloud US or EU, or leave out ScheduledAt to send the email now.",
            field: ScheduledAtField);
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

    static bool IsRepeatableUpload(EmailException error) => error.Code == "attachment_upload_failed";

    static bool IsRetryable(EmailException error)
    {
        return error.StatusCode switch
        {
            null => error.Code == "network_error",
            HttpStatusCode.Conflict => error.Code == "idempotency_key_in_flight",
            (HttpStatusCode)429 => error.Code is not ("quota_limited" or "upload_quota_exceeded"),
            HttpStatusCode.ServiceUnavailable => true,
            >= HttpStatusCode.InternalServerError => IsRepeatableUpload(error),
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
        CancellationToken cancellationToken,
        TimeSpan? attemptTimeout = null)
    {
        var waitFor = attemptTimeout ?? RequestTimeout;
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

        using var deadline = DeadlineAfter(waitFor);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient!.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw NetworkError(
                deadline.IsCancellationRequested
                    ? $"The request to PostHog timed out after {waitFor.TotalSeconds:0.###}s."
                    : $"The request to PostHog timed out after {_httpClient!.Timeout.TotalSeconds:0.###}s, the Timeout "
                      + "of the app's HttpClient.",
                e);
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
            return new Reply(
                response.StatusCode,
                ParseObject(await ReadTextOrNullAsync(response.Content, cancellationToken).ConfigureAwait(false)),
                ReadRetryAfter(response));
        }
    }

    static async Task<string?> ReadTextOrNullAsync(HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
#if NET5_0_OR_GREATER
            return await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            return await content.ReadAsStringAsync().ConfigureAwait(false);
#endif
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    CancellationTokenSource DeadlineAfter(TimeSpan wait)
#if NET8_0_OR_GREATER
        => new(wait, _timeProvider);
#else
        => _timeProvider.CreateCancellationTokenSource(wait);
#endif

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

    static JsonElement? ParseObject(string? text)
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

    SendEmailResult ParseSendReply(Reply reply, DateTimeOffset? scheduledAt)
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
        var warnings = ReadWarnings(reply.Fields);
        LogWarnings(warnings);

        var replayed = reply.Fields is { } replyFields
                       && replyFields.TryGetProperty("replayed", out var replayedValue)
                       && replayedValue.ValueKind == JsonValueKind.True;
        var (status, confirmedAt) = scheduledAt is { } requested
            ? ConfirmSchedule(reply, id, requested)
            : ("queued", null);
        return new SendEmailResult
        {
            Id = id,
            Replayed = replayed,
            Warnings = warnings,
            Status = status,
            ScheduledAt = confirmedAt,
        };
    }

    static List<EmailWarning> ReadWarnings(JsonElement? fields)
    {
        var warnings = new List<EmailWarning>();
        if (fields is { } answer
            && answer.TryGetProperty("warnings", out var list)
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

        return warnings;
    }

    void LogWarnings(IEnumerable<EmailWarning> warnings)
    {
        foreach (var warning in warnings)
        {
            _logger.LogWarningEmailWarning(warning.Code, warning.Message);
        }
    }

    /// <summary>
    /// Checks that the 202 for a send with <c>scheduled_at</c> confirms the requested time, or says the time had
    /// passed with <c>scheduled_at: null</c>. Any other answer, such as one from a PostHog that dropped the field or
    /// sends a time that is still ahead at once, throws <c>schedule_not_confirmed</c> with the accepted email's
    /// message ID. <see cref="MaxClockDrift"/> allows for this machine's clock running behind PostHog's.
    /// </summary>
    (string Status, DateTimeOffset? ScheduledAt) ConfirmSchedule(Reply reply, string id, DateTimeOffset requested)
    {
        var fields = reply.Fields!.Value;
        if (fields.TryGetProperty(ScheduledAtField, out var answered))
        {
            switch (Text(fields, "status"))
            {
                case "queued" when answered.ValueKind == JsonValueKind.Null
                                   && requested - _timeProvider.GetUtcNow() <= MaxClockDrift:
                    return ("queued", null);
                case "scheduled" when AnsweredTime(answered) is { } confirmed && confirmed.UtcTicks == requested.UtcTicks:
                    return ("scheduled", requested);
            }
        }

        throw new EmailException(
            "schedule_not_confirmed",
            $"PostHog accepted email {id} without confirming its ScheduledAt, so it may go out at another time.",
            reply.Status,
            fix: $"Read the email's status with GetAsync(\"{id}\") before you send it again: a send with another "
                 + "IdempotencyKey makes a second email.",
            field: ScheduledAtField,
            messageId: id);
    }

    static DateTimeOffset? AnsweredTime(JsonElement answered)
        => StringValue(answered) is { Length: > 0 } text
           && HasOffset(text)
           && DateTimeOffset.TryParseExact(
               text,
               AnsweredTimeFormats,
               CultureInfo.InvariantCulture,
               DateTimeStyles.AssumeUniversal,
               out var time)
            ? time
            : null;

    static bool HasOffset(string time)
        => time[time.Length - 1] == 'Z'
           || (time.Length > 6 && time[time.Length - 6] is '+' or '-' && time[time.Length - 3] == ':');

    // GetString throws on an unpaired surrogate escape, which would hide an accepted email and its message ID.
    static string? StringValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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

    static EmailException ReplyError(Reply reply, string defaultCode = "api_error", string? defaultField = null)
    {
        return new EmailException(
            Text(reply.Fields, "code") ?? defaultCode,
            Text(reply.Fields, "detail") ?? $"PostHog answered HTTP {(int)reply.Status}.",
            reply.Status,
            why: Text(reply.Fields, "why"),
            fix: Text(reply.Fields, "fix"),
            docsUrl: DocsUrl(reply),
            field: Text(reply.Fields, "attr") ?? defaultField,
            retryAfter: reply.RetryAfter);
    }

    static Uri? DocsUrl(Reply reply)
        => Text(reply.Fields, "docs_url") is { } docs && Uri.TryCreate(docs, UriKind.Absolute, out var uri)
            ? uri
            : null;

    static string? Text(JsonElement? element, string name)
        => element is { ValueKind: JsonValueKind.Object } value
           && value.TryGetProperty(name, out var property)
           && StringValue(property) is { Length: > 0 } text
            ? text
            : null;

    static DateTimeOffset? Time(JsonElement? element, string name)
        => Text(element, name) is { } text
           && DateTimeOffset.TryParse(
               text,
               CultureInfo.InvariantCulture,
               DateTimeStyles.RoundtripKind,
               out var time)
            ? time
            : null;

    static PreparedSend PrepareSend(SendEmailOptions email, int defaultMaxRetries)
    {
        NotNull(email);
        var idempotencyKey = RecipientKey(email.To, email.DistinctId, email.IdempotencyKey, string.Empty, nameof(email));
        if (email.MaxRetries < 0)
        {
            throw new ArgumentException("MaxRetries must not be negative.", nameof(email));
        }

        var (body, attachments) = email switch
        {
            SendTemplateEmailOptions template => (TemplateBody(template), HashedAttachments.None),
            SendCodeEmailOptions code => CodeBody(code),
            _ => throw new ArgumentException(
                "Send a SendTemplateEmailOptions or a SendCodeEmailOptions.",
                nameof(email)),
        };
        var scheduledAt = email.ScheduledAt is { } requested
            ? CeilingToMillisecond(requested)
              ?? throw new ArgumentException("ScheduledAt must be a time PostHog can keep to the millisecond.", nameof(email))
            : (DateTimeOffset?)null;
        if (scheduledAt is { } due)
        {
            body[ScheduledAtField] = due.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        return new PreparedSend(
            JsonSerializer.Serialize(body, JsonSerializerHelper.Options),
            attachments,
            idempotencyKey,
            email.MaxRetries ?? defaultMaxRetries,
            scheduledAt);
    }

    /// <summary>
    /// Checks one recipient's address, distinct ID and idempotency key, and returns the key, made by the client when
    /// the caller gave none.
    /// </summary>
    static string RecipientKey(string? to, string? distinctId, string? idempotencyKey, string where, string paramName)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            throw new ArgumentException($"{where}To must be one email address.", paramName);
        }

        if (distinctId is { Length: 0 })
        {
            throw new ArgumentException($"{where}DistinctId must not be empty. Leave it null to leave it out.", paramName);
        }

        if (idempotencyKey is { } key && !IsVisibleAscii(key, MaxIdempotencyKeyLength))
        {
            throw new ArgumentException(
                $"{where}IdempotencyKey must be 1 to 255 visible ASCII characters without spaces.",
                paramName);
        }

        return idempotencyKey ?? Guid.NewGuid().ToString("D");
    }

    /// <summary>
    /// The UTC time PostHog keeps for <paramref name="time"/>: rounded up to the next millisecond, so the email never
    /// goes out before the time the caller asked for. <c>null</c> when that time is past the last one .NET holds.
    /// </summary>
    static DateTimeOffset? CeilingToMillisecond(DateTimeOffset time)
    {
        var belowAMillisecond = time.UtcTicks % TimeSpan.TicksPerMillisecond;
        var ticks = belowAMillisecond == 0 ? time.UtcTicks : time.UtcTicks + (TimeSpan.TicksPerMillisecond - belowAMillisecond);
        return ticks > DateTimeOffset.MaxValue.UtcTicks ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    static Dictionary<string, object?> TemplateBody(SendTemplateEmailOptions email)
    {
        CheckTemplateId(email.TemplateId, nameof(email));

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

    static void CheckTemplateId(Guid templateId, string paramName)
    {
        if (templateId == Guid.Empty)
        {
            throw new ArgumentException("TemplateId must be the ID of a template made in PostHog.", paramName);
        }
    }

    /// <summary>
    /// Checks the content fields that a code send and a code batch share, and returns them as the request's first
    /// fields, with <paramref name="to"/> when the request has one recipient.
    /// </summary>
    static Dictionary<string, object?> CodeContent(
        string? templateKey,
        string? to,
        string? subject,
        string? html,
        string? text,
        string? from,
        IReadOnlyList<string>? replyTo,
        string request,
        string paramName)
    {
        if (!IsTemplateKey(templateKey))
        {
            throw new ArgumentException(
                "TemplateKey can only have letters, digits, hyphens (-) and underscores (_), at most 400.",
                paramName);
        }

        if (subject is null)
        {
            throw new ArgumentException($"A {request} with TemplateKey needs a Subject.", paramName);
        }

        if (html is null && text is null)
        {
            throw new ArgumentException($"A {request} with TemplateKey needs Html, Text or both.", paramName);
        }

        if (replyTo is not null && replyTo.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("ReplyTo must not hold a blank address.", paramName);
        }

        var body = new Dictionary<string, object?> { ["template_key"] = templateKey };
        if (to is not null)
        {
            body["to"] = to;
        }

        body["subject"] = subject;
        if (html is not null)
        {
            body["html"] = html;
        }

        if (text is not null)
        {
            body["text"] = text;
        }

        if (from is not null)
        {
            body["from"] = from;
        }

        if (replyTo is not null)
        {
            body["reply_to"] = replyTo;
        }

        return body;
    }

    static (Dictionary<string, object?> Body, HashedAttachments Attachments) CodeBody(SendCodeEmailOptions email)
    {
        var body = CodeContent(
            email.TemplateKey,
            email.To,
            email.Subject,
            email.Html,
            email.Text,
            email.From,
            email.ReplyTo,
            "send",
            nameof(email));
        if (email.Cc is { } cc && cc.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Cc must not hold a blank address.", nameof(email));
        }

        if (email.Bcc is { } bcc && bcc.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Bcc must not hold a blank address.", nameof(email));
        }

        if (email.Attachments is { } attachments && attachments.Any(file => file is null))
        {
            throw new ArgumentException("Attachments must not hold a null file.", nameof(email));
        }

        if (email.Attachments is { } named && named.Any(file => string.IsNullOrWhiteSpace(file.Filename)))
        {
            throw new ArgumentException("Every attachment needs a Filename, such as invoice.pdf.", nameof(email));
        }

        if (email.Attachments is { } filled && filled.Any(file => file.Content.IsEmpty))
        {
            throw new ArgumentException(
                "Every attachment needs Content: PostHog doesn't send empty files.",
                nameof(email));
        }

        var hashed = email.Attachments is { Count: > 0 } files
            ? HashedAttachments.Hash(files, email.Subject, email.Html, email.Text)
            : HashedAttachments.None;
        if (email.Cc is { Count: > 0 })
        {
            body["cc"] = email.Cc;
        }

        if (email.Bcc is { Count: > 0 })
        {
            body["bcc"] = email.Bcc;
        }

        if (hashed.Files.Count > 0)
        {
            body["attachments"] = hashed.Files.Select(ReferenceBody).ToList();
        }

        if (email.CaptureEngagementIfSet is { } captureEngagement)
        {
            body["capture_engagement"] = captureEngagement;
        }

        AddSensitiveAndTracking(body, email.Sensitive, email.Tracking);
        if (email.DistinctId is not null)
        {
            body["distinct_id"] = email.DistinctId;
        }

        return (body, hashed);
    }

    static void AddSensitiveAndTracking(Dictionary<string, object?> body, bool? sensitive, EmailTracking? tracking)
    {
        if (sensitive is { } isSensitive)
        {
            body["sensitive"] = isSensitive;
        }

        if (tracking is not null)
        {
            body["tracking"] = tracking;
        }
    }

    static Dictionary<string, object> ReferenceBody(HashedAttachment attachment)
    {
        var body = new Dictionary<string, object>
        {
            ["sha256"] = attachment.Sha256,
            ["filename"] = attachment.File.Filename,
        };
        if (attachment.File.ContentType is not null)
        {
            body["content_type"] = attachment.File.ContentType;
        }

        if (attachment.File.ContentId is not null)
        {
            body["content_id"] = attachment.File.ContentId;
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

    readonly record struct PreparedSend(
        string Body,
        HashedAttachments Attachments,
        string IdempotencyKey,
        int MaxRetries,
        DateTimeOffset? ScheduledAt);

    sealed record UploadTarget(Uri Url, HttpMethod Method, IReadOnlyDictionary<string, string> Headers);

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
