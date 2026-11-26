using System.Globalization;
using System.Net;
using System.Text.Json;
using PostHog.Json;
using static PostHog.Library.Ensure;

namespace PostHog.Emails;

internal sealed partial class EmailSender
{
    const string BatchPath = "/api/messaging/send/email_batch/";
    const int BatchMaxRecipients = 100;
    const string ResendWithTheSameKey = "Send it again with the same IdempotencyKey within 24 hours of the key's first use, so it goes out once.";
    const string SendSingly = "send each email the batch didn't queue with SendAsync and its recipient's IdempotencyKey.";
    const string SplitTheBatch = "Split the recipients into smaller batches and send each with BatchAsync.";
    const string ReadEachResult = "Read each recipient's result in Results, and send each unconfirmed one again with the IdempotencyKey its result holds, within 24 hours of the key's first use, so each email goes out once.";
    const string UnconfirmedMessage = "PostHog didn't confirm this email, so it may still go out.";
    const string SendEachOnItsOwn = "If this project can send email from code, send each recipient on its own with SendAsync and its IdempotencyKey until batch sends are available.";
    const string CheckTheTemplate = "Check TemplateId. If it is right, send each recipient on its own with SendAsync and its IdempotencyKey until batch sends are available.";
    const string ShrinkTheEmail = "Make the email's content or TemplateData smaller, so the request fits PostHog's size limit.";

    static readonly TimeSpan BatchAttemptTimeout = TimeSpan.FromSeconds(45);

    static readonly JsonSerializerOptions BatchBodyOptions = new(JsonSerializerHelper.Options)
    {
        MaxDepth = DefaultMaxDepth + RecipientRowLevels,
    };

    const int DefaultMaxDepth = 64;
    const int RecipientRowLevels = 2;

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
                            "The PostHog client is shut down, so it didn't retry the batch.");
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    run.StartAttempt();
                    Reply reply;
                    var dispatched = false;
                    try
                    {
                        reply = await RequestAsync(
                                HttpMethod.Post,
                                url,
                                secretKey,
                                run.Body,
                                idempotencyKey: null,
                                cancellationToken,
                                attemptTimeout,
                                onDispatch: () => dispatched = true)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (dispatched && cancellationToken.IsCancellationRequested)
                    {
                        run.Lost();
                        throw;
                    }
                    catch (EmailException e) when (e.Code == "network_error")
                    {
                        run.Lost();
                        throw;
                    }
                    catch (EmailException e)
                    {
                        run.Lost();
                        throw NetworkError("The request to PostHog failed before it was answered.", e);
                    }
                    catch (Exception e) when (!dispatched && e is not EmailException && !IsCallerCancellation(e, cancellationToken))
                    {
                        throw new EmailException(
                            "unexpected_error",
                            run.MayHaveQueued
                                ? "The retry to PostHog couldn't start."
                                : "The request to PostHog couldn't start, so no email was sent.",
                            innerException: e);
                    }
                    catch (Exception e) when (e is not EmailException && !IsCallerCancellation(e, cancellationToken))
                    {
                        run.Lost();
                        throw NetworkError("The request to PostHog failed before it was answered.", e);
                    }

                    run.Answer(reply);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (EmailException error)
        {
            run.Fail(error);
            LogWarnings(run.Warnings);
            throw new EmailBatchException(
                run.EarlierAttemptMayHaveQueued && error.Code != "network_error" ? AfterAnEarlierBatchAttempt(error) : error,
                run.Outcome(),
                run.Warnings);
        }
        catch (OperationCanceledException e) when (run.MayHaveQueued && cancellationToken.IsCancellationRequested)
        {
            run.Cancel();
            LogWarnings(run.Warnings);
            throw new EmailBatchCanceledException(e, run.Outcome(), run.Warnings, cancellationToken);
        }

        LogWarnings(run.Warnings);
        return new SendBatchResult { Results = run.Outcome(), Warnings = run.Warnings };
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

    static EmailException AfterAnEarlierBatchAttempt(EmailException refusal) => new(
        refusal.Code,
        $"An earlier attempt may have queued some of these emails, so they may still go out. PostHog refused the retry with {refusal.Code}.",
        refusal.StatusCode,
        refusal.Why,
        ReadEachResult,
        refusal.DocsUrl,
        refusal.Field,
        refusal.RetryAfter,
        refusal,
        refusal.MessageId);

    static EmailException NotConfirmed(EmailException answer) => new(
        answer.Code,
        "PostHog didn't confirm these emails, so they may still go out.",
        answer.StatusCode,
        answer.Why,
        ReadEachResult,
        answer.DocsUrl,
        answer.Field,
        answer.RetryAfter,
        answer,
        answer.MessageId);

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

        CheckRecipientCount(batch.Recipients?.Count ?? 0, nameof(batch));
        var recipients = batch.Recipients!.ToList();
        CheckRecipientCount(recipients.Count, nameof(batch));

        Dictionary<string, object?> shared;
        switch (batch)
        {
            case SendTemplateBatchOptions template:
                CheckTemplateId(template.TemplateId, nameof(batch));
                shared = new Dictionary<string, object?>
                {
                    ["template_id"] = template.TemplateId.ToString("D"),
                    ["message_data"] = JsonSerializer.SerializeToElement<object>(
                        template.TemplateData ?? new Dictionary<string, object>(),
                        JsonSerializerHelper.Options),
                };
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
                break;
            default:
                throw new ArgumentException("Send a SendTemplateBatchOptions or a SendCodeBatchOptions.", nameof(batch));
        }

        var rows = BatchRows(recipients!, batch is SendTemplateBatchOptions, nameof(batch));
        shared["recipients"] = rows.Select(row => row.Wire).ToList();
        return new BatchRun(
            JsonSerializer.Serialize(shared, BatchBodyOptions),
            rows,
            batch.MaxRetries ?? defaultMaxRetries,
            batch is SendTemplateBatchOptions);
    }

    static void CheckRecipientCount(int count, string paramName)
    {
        if (count <= 0)
        {
            throw new ArgumentException("Recipients must hold at least one recipient.", paramName);
        }

        if (count > BatchMaxRecipients)
        {
            throw new ArgumentException(
                $"Recipients holds {count} recipients, and a batch takes at most {BatchMaxRecipients}. "
                + "Split them into batches of 100 or fewer.",
                paramName);
        }
    }

    static List<BatchRow> BatchRows(List<BatchRecipient> recipients, bool isTemplate, string paramName)
    {
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
            Wire = new Dictionary<string, object?> { ["to"] = recipient.To, ["idempotency_key"] = idempotencyKey };
            if (recipient.DistinctId is not null)
            {
                Wire["distinct_id"] = recipient.DistinctId;
            }

            if (recipient.TemplateData is not null)
            {
                Wire["message_data"] = JsonSerializer.SerializeToElement<object>(
                    recipient.TemplateData,
                    JsonSerializerHelper.Options);
            }
        }

        public int Index { get; }

        public string To { get; }

        public string IdempotencyKey { get; }

        public Dictionary<string, object?> Wire { get; }

        public string? Status { get; private set; }

        string? MessageId { get; set; }

        bool Replayed { get; set; }

        EmailException? Error { get; set; }

        public void Set(string? status, string? messageId = null, bool replayed = false, EmailException? error = null)
        {
            (Status, MessageId, Replayed, Error) = (status, messageId, replayed, error);
        }

        public BatchEmailResult Result() => new()
        {
            Index = Index,
            To = To,
            IdempotencyKey = IdempotencyKey,
            Status = Status ?? throw new InvalidOperationException($"Recipient {Index} has no result yet."),
            MessageId = MessageId,
            Replayed = Replayed,
            Error = Status == "unconfirmed" ? AsUnconfirmed(Error) : Error,
        };

        static EmailException? AsUnconfirmed(EmailException? error)
            => error is null || (error.Message == UnconfirmedMessage && error.Fix == ResendWithTheSameKey)
                ? error
                : new EmailException(
                    error.Code,
                    UnconfirmedMessage,
                    error.StatusCode,
                    error.Why,
                    ResendWithTheSameKey,
                    error.DocsUrl,
                    error.Field,
                    error.RetryAfter,
                    error,
                    error.MessageId);
    }

    sealed class BatchRun(string body, List<BatchRow> rows, int maxRetries, bool fromTemplate)
    {
        readonly List<EmailWarning> _warnings = [];
        bool _mayHaveQueued;

        public string Body { get; } = body;

        public int MaxRetries { get; } = maxRetries;

        public IReadOnlyList<EmailWarning> Warnings => _warnings;

        public void StartAttempt()
        {
            EarlierAttemptMayHaveQueued = _mayHaveQueued;
            foreach (var row in rows)
            {
                row.Set(null);
            }
        }

        public bool MayHaveQueued => _mayHaveQueued;

        public bool EarlierAttemptMayHaveQueued { get; private set; }

        public void Lost()
        {
            _mayHaveQueued = true;
            Unconfirm(rows);
        }

        public void Cancel() => Unconfirm(rows.Where(row => row.Status is null));

        static void Unconfirm(IEnumerable<BatchRow> unanswered)
        {
            foreach (var row in unanswered)
            {
                row.Set(
                    "unconfirmed",
                    error: new EmailException(
                        "delivery_unavailable",
                        UnconfirmedMessage,
                        fix: ResendWithTheSameKey));
            }
        }

        public void Answer(Reply reply)
        {
            if (!IsSuccess(reply.Status))
            {
                var error = BatchReplyError(reply, rows.Count, fromTemplate, EarlierAttemptMayHaveQueued);
                if ((int)reply.Status >= 500)
                {
                    Lost();
                }
                else if (UnconfirmedRequestCodes.Contains(error.Code)
                         || (reply.Status == HttpStatusCode.Conflict && error.Code != "batch_unavailable"))
                {
                    UnconfirmWith(error);
                    if (!EarlierAttemptMayHaveQueued)
                    {
                        throw NotConfirmed(error);
                    }
                }

                throw error;
            }

            if (reply.Fields is not { } fields
                || !TryField(fields, "results", out var slots)
                || slots.ValueKind != JsonValueKind.Array
                || slots.GetArrayLength() != rows.Count)
            {
                var error = new EmailException(
                    "api_error",
                    "PostHog answered the batch without a result for each recipient.",
                    reply.Status);
                UnconfirmWith(error);
                throw error;
            }

            var position = 0;
            foreach (var slot in slots.EnumerateArray())
            {
                Settle(rows[position++], slot, _mayHaveQueued);
            }

            foreach (var warning in ReadWarnings(fields))
            {
                if (!_warnings.Any(seen => (seen.Code, seen.Message, seen.Fix) == (warning.Code, warning.Message, warning.Fix)))
                {
                    _warnings.Add(warning);
                }
            }
        }

        public void Fail(EmailException error)
        {
            foreach (var row in rows.Where(row => row.Status is null))
            {
                row.Set(_mayHaveQueued ? "unconfirmed" : "failed", error: error);
            }
        }

        public List<BatchEmailResult> Outcome() => rows.Select(row => row.Result()).ToList();

        void UnconfirmWith(EmailException error)
        {
            _mayHaveQueued = true;
            foreach (var row in rows)
            {
                row.Set("unconfirmed", error: error);
            }
        }

        static void Settle(BatchRow row, JsonElement slot, bool mayHaveQueued)
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
            row.Set(
                mayHaveQueued || UnconfirmedSlotCodes.Contains(answer.Code) ? "unconfirmed" : "failed",
                messageId,
                error: answer);
        }

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
    }

    static EmailException BatchReplyError(Reply reply, int recipients, bool fromTemplate, bool earlierAttemptMayHaveQueued)
    {
        var code = Text(reply.Fields, "code");
        if (reply.Status == HttpStatusCode.NotFound
            && code is null or "not_found" or "api_error"
            && Text(reply.Fields, "attr") is null)
        {
            return new EmailException(
                "not_found",
                (fromTemplate
                    ? "PostHog found no template with this TemplateId, or batch sends aren't available for this project"
                    : "Batch sends, or sending from code, aren't available for this project")
                + (earlierAttemptMayHaveQueued ? "." : ", so PostHog sent no email of this request."),
                reply.Status,
                why: fromTemplate
                    ? "PostHog answers the same 404 for a template it can't find and for a project without batch sends."
                    : null,
                fix: fromTemplate ? CheckTheTemplate : SendEachOnItsOwn,
                docsUrl: DocsUrl(reply),
                retryAfter: reply.RetryAfter);
        }

        code ??= reply.Status == HttpStatusCode.NotFound ? "not_found" : "api_error";
        var fix = code == "batch_unavailable"
            ? "Until batch sends are on for the project, " + SendSingly
            : Text(reply.Fields, "fix")
              ?? (reply.Status == HttpStatusCode.RequestEntityTooLarge ? (recipients > 1 ? SplitTheBatch : ShrinkTheEmail) : null);
        return new EmailException(
            code,
            Text(reply.Fields, "detail") ?? $"PostHog answered HTTP {(int)reply.Status}.",
            reply.Status,
            why: Text(reply.Fields, "why"),
            fix: fix,
            docsUrl: DocsUrl(reply),
            field: Text(reply.Fields, "attr") is { } attr ? FieldInRecipients(attr, recipients) : null,
            retryAfter: reply.RetryAfter);
    }

    static string FieldInRecipients(string attr, int recipients)
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

        return int.TryParse(position, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < recipients
            ? attr
            : "recipients";
    }
}
