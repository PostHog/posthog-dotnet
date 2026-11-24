using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using PostHog.Json;
using static PostHog.Library.Ensure;

namespace PostHog.Emails;

internal sealed partial class EmailSender
{
    const string BatchPath = "/api/messaging/send/email_batch/";
    const int BatchMaxRecipients = 100;
    const int BatchMaxBodyBytes = 4 * 1024 * 1024;
    const int PipelineMaxCopiedBytes = 16 * 1024 * 1024;
    const int EmptyObjectBytes = 2;
    const string ResendWithTheSameKey = "Send it again with the same IdempotencyKey within 24 hours, so it goes out once.";
    const string SendSingly = "send each email the batch didn't queue with SendAsync and its recipient's IdempotencyKey.";

    static readonly TimeSpan BatchAttemptTimeout = TimeSpan.FromSeconds(45);

    static readonly HashSet<string> UnconfirmedRequestCodes =
        new(StringComparer.Ordinal) { "idempotency_key_in_flight", "delivery_unavailable" };

    static readonly HashSet<string> UnconfirmedSlotCodes =
        new(UnconfirmedRequestCodes, StringComparer.Ordinal) { "api_error" };

    public async Task<SendBatchResult> BatchAsync(SendBatchOptions batch, CancellationToken cancellationToken = default)
    {
        var run = PrepareBatch(batch, MaxRetriesOption);
        using var _ = Enter("no email was sent");
        var (appHost, secretKey) = Connect("no email was sent", SendNeedsSecretKey);
        var url = new Uri(appHost + BatchPath);
        var attemptTimeout = RequestTimeout > BatchAttemptTimeout ? RequestTimeout : BatchAttemptTimeout;
        for (var position = 0; position < run.Chunks.Count; position++)
        {
            if (position > 0 && IsClosed)
            {
                throw Failure(run, run.ClosedAt(position));
            }

            var chunk = run.Chunks[position];
            var body = run.Body(chunk);
            var attempts = 0;
            try
            {
                await WithRetriesAsync(
                    run.MaxRetries,
                    async () =>
                    {
                        if (attempts++ > 0 && IsClosed)
                        {
                            throw new EmailException(
                                "client_closed",
                                "The PostHog client is shut down, so it didn't retry this request of the batch.");
                        }

                        Reply reply;
                        try
                        {
                            reply = await RequestAsync(
                                    HttpMethod.Post,
                                    url,
                                    secretKey,
                                    body,
                                    idempotencyKey: null,
                                    cancellationToken,
                                    attemptTimeout)
                                .ConfigureAwait(false);
                        }
                        catch (EmailException e) when (e.Code == "network_error")
                        {
                            chunk.Lost();
                            throw;
                        }
                        catch (Exception e) when (e is not EmailException && !IsCallerCancellation(e, cancellationToken))
                        {
                            chunk.Lost();
                            throw NetworkError("The request to PostHog failed before it was answered.", e);
                        }

                        run.Answer(chunk, reply);
                        return true;
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (EmailException error)
            {
                run.Stop(position, error);
                throw Failure(run, error);
            }
        }

        LogWarnings(run.Warnings);
        return new SendBatchResult { Results = run.Outcome(), Warnings = run.Warnings.ToList() };
    }

    bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _closed;
            }
        }
    }

    EmailBatchException Failure(BatchRun run, EmailException error)
    {
        LogWarnings(run.Warnings);
        return new EmailBatchException(error, run.Outcome(), run.Warnings.ToList());
    }

    static bool IsMessageId(string text) => Guid.TryParseExact(text, "D", out _);

    static bool IsCallerCancellation(Exception error, CancellationToken cancellationToken)
        => error is OperationCanceledException && cancellationToken.IsCancellationRequested;

    static BatchRun PrepareBatch(SendBatchOptions batch, int defaultMaxRetries)
    {
        NotNull(batch);
        if (batch.MaxRetries < 0)
        {
            throw new ArgumentException("MaxRetries must not be negative.", nameof(batch));
        }

        Dictionary<string, object?> shared;
        long copiedPerRecipient;
        switch (batch)
        {
            case SendTemplateBatchOptions template:
                CheckTemplateId(template.TemplateId, nameof(batch));
                var sharedData = JsonSerializer.SerializeToElement<object>(
                    template.TemplateData ?? new Dictionary<string, object>(),
                    JsonSerializerHelper.Options);
                shared = new Dictionary<string, object?>
                {
                    ["template_id"] = template.TemplateId.ToString("D"),
                    ["message_data"] = sharedData,
                };
                copiedPerRecipient = Utf8Bytes(sharedData);
                break;
            case SendCodeBatchOptions code:
                shared = CheckedCodeContent(
                    code.TemplateKey,
                    to: null,
                    code.Subject,
                    code.Html,
                    code.Text,
                    code.From,
                    code.ReplyTo,
                    "batch",
                    nameof(batch));
                AddSensitiveAndTracking(shared, code.Sensitive, code.Tracking);
                copiedPerRecipient = EmptyObjectBytes
                                     + Utf8Bytes(shared)
                                     + (string.IsNullOrEmpty(code.Text) && code.Html is not null ? Utf8Bytes(code.Html) : 0);
                break;
            default:
                throw new ArgumentException("Send a SendTemplateBatchOptions or a SendCodeBatchOptions.", nameof(batch));
        }

        var rows = BatchRows(batch.Recipients, batch is SendTemplateBatchOptions, nameof(batch));
        shared["recipients"] = Array.Empty<object>();
        var empty = JsonSerializer.Serialize(shared, JsonSerializerHelper.Options);
        return new BatchRun(
            empty.Substring(0, empty.Length - "]}".Length),
            copiedPerRecipient,
            rows,
            batch.MaxRetries ?? defaultMaxRetries);
    }

    static int Utf8Bytes(object? value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerHelper.Options).Length;

    static List<BatchRow> BatchRows(IReadOnlyList<BatchRecipient>? recipients, bool isTemplate, string paramName)
    {
        if (recipients is not { Count: > 0 })
        {
            throw new ArgumentException("Recipients must hold at least one recipient.", paramName);
        }

        var firstWithKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = new List<BatchRow>(recipients.Count);
        for (var index = 0; index < recipients.Count; index++)
        {
            var where = $"Recipients[{index}]";
            var recipient = recipients[index] ?? throw new ArgumentException($"{where} must not be null.", paramName);
            var key = CheckedRecipientKey(recipient.To, recipient.DistinctId, recipient.IdempotencyKey, where + ": ", paramName);
            if (recipient.TemplateData is not null && !isTemplate)
            {
                throw new ArgumentException(
                    $"{where} has TemplateData, but a batch with TemplateKey sends the same content to every "
                    + "recipient. Send personalized content with a SendTemplateBatchOptions batch or with SendAsync.",
                    paramName);
            }

            if (firstWithKey.TryGetValue(key, out var first))
            {
                throw new ArgumentException(
                    $"{where} has the same IdempotencyKey as Recipients[{first}]. Give each recipient its own key.",
                    paramName);
            }

            firstWithKey[key] = index;
            rows.Add(new BatchRow(index, recipient, key));
        }

        return rows;
    }

    sealed class BatchRow
    {
        public BatchRow(int index, BatchRecipient recipient, string idempotencyKey)
        {
            Index = index;
            To = recipient.To;
            IdempotencyKey = idempotencyKey;
            JsonElement? messageData = recipient.TemplateData is null
                ? null
                : JsonSerializer.SerializeToElement<object>(recipient.TemplateData, JsonSerializerHelper.Options);
            Json = JsonSerializer.Serialize(WireOf(recipient, idempotencyKey, messageData), JsonSerializerHelper.Options);
            Bytes = Encoding.UTF8.GetByteCount(Json);
            OwnCopiedBytes = messageData is { } data ? Utf8Bytes(data) : EmptyObjectBytes;
        }

        public int Index { get; }

        public string To { get; }

        public string IdempotencyKey { get; }

        public string Json { get; }

        public int Bytes { get; }

        public int OwnCopiedBytes { get; }

        public string? Status { get; private set; }

        public string? MessageId { get; private set; }

        public EmailException? Error { get; private set; }

        public bool IsUnconfirmed => Status == "unconfirmed";

        bool Replayed { get; set; }

        EmailException? LaterError { get; set; }

        static Dictionary<string, object?> WireOf(BatchRecipient recipient, string idempotencyKey, JsonElement? messageData)
        {
            var wire = new Dictionary<string, object?> { ["to"] = recipient.To, ["idempotency_key"] = idempotencyKey };
            if (recipient.DistinctId is not null)
            {
                wire["distinct_id"] = recipient.DistinctId;
            }

            if (messageData is { } data)
            {
                wire["message_data"] = data;
            }

            return wire;
        }

        public void Set(
            string status,
            string? messageId = null,
            bool replayed = false,
            EmailException? error = null,
            EmailException? laterError = null)
        {
            (Status, MessageId, Replayed, Error, LaterError) = (status, messageId, replayed, error, laterError);
        }

        public void AddLaterError(EmailException later) => Set("unconfirmed", MessageId, error: Error, laterError: later);

        public BatchEmailResult Result() => new()
        {
            Index = Index,
            To = To,
            IdempotencyKey = IdempotencyKey,
            Status = Status ?? throw new InvalidOperationException($"Recipient {Index} has no result yet."),
            MessageId = MessageId,
            Replayed = Replayed,
            Error = IsUnconfirmed ? WithResendFix(Error) : Error,
            LaterError = LaterError,
        };

        static EmailException? WithResendFix(EmailException? error) => error is null || error.Fix == ResendWithTheSameKey
            ? error
            : new EmailException(
                error.Code,
                error.Message,
                error.StatusCode,
                error.Why,
                ResendWithTheSameKey,
                error.DocsUrl,
                error.Field,
                error.RetryAfter,
                error.InnerException,
                error.MessageId);
    }

    sealed record BatchChunk(IReadOnlyList<BatchRow> Rows)
    {
        public void Lost()
        {
            foreach (var row in Rows.Where(row => !row.IsUnconfirmed))
            {
                row.Set("unconfirmed", row.MessageId, error: Unconfirmed(row.MessageId));
            }
        }

        public void UnconfirmWith(EmailException error)
        {
            foreach (var row in Rows.Where(row => !row.IsUnconfirmed))
            {
                row.Set("unconfirmed", row.MessageId, error: error);
            }
        }

        static EmailException Unconfirmed(string? messageId) => new(
            "delivery_unavailable",
            "PostHog didn't confirm this email, so it may still go out.",
            fix: ResendWithTheSameKey,
            messageId: messageId);
    }

    sealed class BatchRun
    {
        readonly string _bodyBeforeRecipients;
        readonly long _sharedCopiedBytes;
        readonly List<BatchRow> _rows;
        readonly List<EmailWarning> _warnings = [];

        public BatchRun(string bodyBeforeRecipients, long sharedCopiedBytes, List<BatchRow> rows, int maxRetries)
        {
            _bodyBeforeRecipients = bodyBeforeRecipients;
            _sharedCopiedBytes = sharedCopiedBytes;
            _rows = rows;
            MaxRetries = maxRetries;
            Chunks = Split();
        }

        public int MaxRetries { get; }

        public List<BatchChunk> Chunks { get; }

        public IReadOnlyList<EmailWarning> Warnings => _warnings;

        List<BatchChunk> Split()
        {
            var emptyBytes = Encoding.UTF8.GetByteCount(_bodyBeforeRecipients) + "]}".Length;
            var chunks = new List<BatchChunk>();
            var current = new List<BatchRow>();
            var size = emptyBytes;
            var copied = 0L;
            foreach (var row in _rows)
            {
                var rowCopied = _sharedCopiedBytes + row.OwnCopiedBytes;
                if (emptyBytes + row.Bytes > BatchMaxBodyBytes || rowCopied > PipelineMaxCopiedBytes)
                {
                    row.Set("failed", error: RecipientTooLarge());
                    continue;
                }

                var added = row.Bytes + (current.Count > 0 ? 1 : 0);
                if (current.Count > 0
                    && (current.Count == BatchMaxRecipients
                        || size + added > BatchMaxBodyBytes
                        || copied + rowCopied > PipelineMaxCopiedBytes))
                {
                    chunks.Add(new BatchChunk(current));
                    current = [];
                    size = emptyBytes;
                    copied = 0;
                    added = row.Bytes;
                }

                current.Add(row);
                size += added;
                copied += rowCopied;
            }

            if (current.Count > 0)
            {
                chunks.Add(new BatchChunk(current));
            }

            return chunks;
        }

        public string Body(BatchChunk chunk)
            => _bodyBeforeRecipients
               + string.Join(",", chunk.Rows.Select(row => row.Json))
               + "]}";

        public void Answer(BatchChunk chunk, Reply reply)
        {
            if (!IsSuccess(reply.Status))
            {
                var error = BatchReplyError(reply, chunk.Rows);
                if ((int)reply.Status >= 500)
                {
                    chunk.Lost();
                }
                else if (UnconfirmedRequestCodes.Contains(error.Code)
                         || (reply.Status == HttpStatusCode.Conflict && Text(reply.Fields, "code") is null))
                {
                    chunk.UnconfirmWith(error);
                }

                throw error;
            }

            if (reply.Fields is not { } fields
                || !TryField(fields, "results", out var slots)
                || slots.ValueKind != JsonValueKind.Array
                || slots.GetArrayLength() != chunk.Rows.Count)
            {
                var error = new EmailException(
                    "api_error",
                    "PostHog answered the batch without a result for each recipient.",
                    reply.Status);
                chunk.UnconfirmWith(error);
                throw error;
            }

            var position = 0;
            foreach (var slot in slots.EnumerateArray())
            {
                Settle(chunk.Rows[position++], slot);
            }

            foreach (var warning in ReadWarnings(fields))
            {
                if (!_warnings.Any(seen => (seen.Code, seen.Message, seen.Fix) == (warning.Code, warning.Message, warning.Fix)))
                {
                    _warnings.Add(warning);
                }
            }
        }

        static void Settle(BatchRow row, JsonElement slot)
        {
            var messageId = Text(slot, "message_id") is { } text && IsMessageId(text) ? text : null;
            JsonElement? error = slot.ValueKind == JsonValueKind.Object
                                 && TryField(slot, "error", out var answered)
                                 && answered.ValueKind != JsonValueKind.Null
                ? answered
                : null;
            if (error is null && Text(slot, "status") == "queued" && messageId is not null)
            {
                var replayed = TryField(slot, "replayed", out var flag) && flag.ValueKind == JsonValueKind.True;
                row.Set("queued", messageId, replayed);
                return;
            }

            var answer = SlotError(error, messageId);
            if (row.IsUnconfirmed)
            {
                row.Set("unconfirmed", messageId ?? row.MessageId, error: row.Error, laterError: answer);
            }
            else if (UnconfirmedSlotCodes.Contains(answer.Code))
            {
                row.Set("unconfirmed", messageId, error: answer);
            }
            else
            {
                row.Set("failed", messageId, error: answer);
            }
        }

        public void Stop(int position, EmailException error)
        {
            var answeredByALaterAttempt = error.StatusCode is { } status && (int)status < 500;
            foreach (var row in Chunks[position].Rows)
            {
                if (!row.IsUnconfirmed)
                {
                    row.Set("failed", error: error);
                }
                else if (answeredByALaterAttempt && !ReferenceEquals(row.Error, error))
                {
                    row.AddLaterError(error);
                }
            }

            SkipFrom(
                position + 1,
                new EmailException(
                    "not_attempted",
                    "An earlier request of the batch failed, so the client sent nothing for this recipient."));
        }

        public EmailException ClosedAt(int position)
        {
            var error = new EmailException(
                "client_closed",
                "The PostHog client is shut down, so the rest of the batch wasn't sent.");
            SkipFrom(position, error);
            return error;
        }

        void SkipFrom(int position, EmailException error)
        {
            foreach (var row in Chunks.Skip(position).SelectMany(chunk => chunk.Rows))
            {
                row.Set("not_attempted", error: error);
            }
        }

        public List<BatchEmailResult> Outcome() => _rows.Select(row => row.Result()).ToList();

        static EmailException SlotError(JsonElement? error, string? messageId)
        {
            if (error is not { ValueKind: JsonValueKind.Object } answer)
            {
                return new EmailException(
                    "api_error",
                    "PostHog answered without a readable result for this email, so it may still go out.",
                    messageId: messageId);
            }

            var code = Text(answer, "code") ?? "api_error";
            HttpStatusCode? status = TryField(answer, "status", out var number)
                                     && number.ValueKind == JsonValueKind.Number
                                     && number.TryGetInt32(out var value)
                ? (HttpStatusCode)value
                : null;
            return new EmailException(
                code,
                Text(answer, "detail") ?? $"PostHog didn't queue this email: {code}.",
                status,
                messageId: messageId);
        }

        static EmailException RecipientTooLarge() => new(
            "content_too_large",
            "This recipient's email is larger than one batch request takes, so it wasn't sent.",
            fix: "Send this recipient's email with SendAsync.",
            field: "recipients");
    }

    static EmailException BatchReplyError(Reply reply, IReadOnlyList<BatchRow> rows)
    {
        var notFound = reply.Status == HttpStatusCode.NotFound;
        var code = Text(reply.Fields, "code") ?? (notFound ? "not_found" : "api_error");
        var fix = code == "batch_unavailable"
            ? "Until batch sends are on for the project, " + SendSingly
            : Text(reply.Fields, "fix") ?? (notFound ? "Check TemplateId, or " + SendSingly : null);
        return new EmailException(
            code,
            Text(reply.Fields, "detail")
            ?? (notFound
                ? "PostHog found no batch send for this request, so no email was sent."
                : $"PostHog answered HTTP {(int)reply.Status}."),
            reply.Status,
            why: Text(reply.Fields, "why")
                 ?? (notFound
                     ? "The template doesn't exist, or batch sends are off for this project or this PostHog version."
                     : null),
            fix: fix,
            docsUrl: DocsUrl(reply),
            field: Text(reply.Fields, "attr") is { } attr ? FieldByPositionInRecipients(attr, rows) : null,
            retryAfter: reply.RetryAfter);
    }

    static string FieldByPositionInRecipients(string attr, IReadOnlyList<BatchRow> rows)
    {
        const string prefix = "recipients__";
        if (!attr.StartsWith(prefix, StringComparison.Ordinal))
        {
            return attr;
        }

        var rest = attr.Substring(prefix.Length);
        var separator = rest.IndexOf("__", StringComparison.Ordinal);
        var position = separator < 0 ? rest : rest.Substring(0, separator);
        if (position.Length == 0 || !position.All(c => c is >= '0' and <= '9'))
        {
            return attr;
        }

        if (!int.TryParse(position, NumberStyles.None, CultureInfo.InvariantCulture, out var inChunk))
        {
            return "recipients";
        }

        if (inChunk >= rows.Count)
        {
            return "recipients";
        }

        return prefix + rows[inChunk].Index.ToString(CultureInfo.InvariantCulture)
               + (separator < 0 ? string.Empty : rest.Substring(separator));
    }
}
