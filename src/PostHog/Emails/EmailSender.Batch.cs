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
    const string StatusByKeyPath = "/api/messaging/emails/status/?idempotency_key=";
    const int BatchMaxRecipients = 100;
    const int BatchMaxBodyBytes = 4 * 1024 * 1024;
    const string SendSingly = "send each email the batch didn't queue with SendAsync and its recipient's IdempotencyKey.";

    // PostHog's email pipeline takes up to 30 seconds to confirm a batch, so a shorter wait would lose its answer.
    static readonly TimeSpan BatchAttemptTimeout = TimeSpan.FromSeconds(45);

    static readonly HashSet<string> UnconfirmedSlotCodes =
        new(StringComparer.Ordinal) { "idempotency_key_in_flight", "delivery_unavailable", "api_error" };

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
                                    chunk.Body,
                                    idempotencyKey: null,
                                    cancellationToken,
                                    attemptTimeout)
                                .ConfigureAwait(false);
                        }
                        catch (EmailException e) when (e.Code == "network_error")
                        {
                            run.Lost(chunk);
                            throw;
                        }

                        run.Answer(chunk, reply);
                        return true;
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (EmailException error)
            {
                run.Stop(position, error);
                await ReadLostAsync(run, appHost, secretKey, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Reads, by its idempotency key, each email that a lost answer left without a message ID, so an email PostHog
    /// queued reads as <c>queued</c>. Stops at the first answer other than "not found", or when the client shuts down.
    /// </summary>
    async Task ReadLostAsync(BatchRun run, string appHost, string secretKey, CancellationToken cancellationToken)
    {
        foreach (var (index, key) in run.LostKeys())
        {
            if (IsClosed)
            {
                return;
            }

            EmailStatus status;
            try
            {
                var url = new Uri(appHost + StatusByKeyPath + Uri.EscapeDataString(key));
                status = ParseStatusReply(
                    await RequestAsync(HttpMethod.Get, url, secretKey, body: null, idempotencyKey: null, cancellationToken)
                        .ConfigureAwait(false));
            }
            catch (EmailException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }
            catch (EmailException)
            {
                return;
            }

            run.Accepted(index, status);
        }
    }

    static BatchRun PrepareBatch(SendBatchOptions batch, int defaultMaxRetries)
    {
        NotNull(batch);
        if (batch.MaxRetries < 0)
        {
            throw new ArgumentException("MaxRetries must not be negative.", nameof(batch));
        }

        Dictionary<string, object?> shared;
        switch (batch)
        {
            case SendTemplateBatchOptions template:
                CheckTemplateId(template.TemplateId, nameof(batch));
                shared = new Dictionary<string, object?>
                {
                    ["template_id"] = template.TemplateId.ToString("D"),
                    ["message_data"] = template.TemplateData ?? new Dictionary<string, object>(),
                };
                break;
            case SendCodeBatchOptions code:
                shared = CodeContent(
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

        var members = BatchMembers(batch.Recipients, batch is SendTemplateBatchOptions, nameof(batch));
        shared["recipients"] = Array.Empty<object>();
        return new BatchRun(
            JsonSerializer.Serialize(shared, JsonSerializerHelper.Options),
            members,
            batch.MaxRetries ?? defaultMaxRetries);
    }

    static List<BatchMember> BatchMembers(IReadOnlyList<BatchRecipient>? recipients, bool isTemplate, string paramName)
    {
        if (recipients is not { Count: > 0 })
        {
            throw new ArgumentException("Recipients must hold at least one recipient.", paramName);
        }

        var firstWithKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var members = new List<BatchMember>(recipients.Count);
        for (var index = 0; index < recipients.Count; index++)
        {
            var where = $"Recipients[{index}]";
            var recipient = recipients[index] ?? throw new ArgumentException($"{where} must not be null.", paramName);
            var key = RecipientKey(recipient.To, recipient.DistinctId, recipient.IdempotencyKey, where + ": ", paramName);
            var wire = new Dictionary<string, object?> { ["to"] = recipient.To, ["idempotency_key"] = key };
            if (recipient.DistinctId is not null)
            {
                wire["distinct_id"] = recipient.DistinctId;
            }

            if (recipient.TemplateData is not null)
            {
                if (!isTemplate)
                {
                    throw new ArgumentException(
                        $"{where} has TemplateData, but a batch with TemplateKey sends the same content to every "
                        + "recipient. Send personalized content with a SendTemplateBatchOptions batch or with SendAsync.",
                        paramName);
                }

                wire["message_data"] = recipient.TemplateData;
            }

            if (firstWithKey.TryGetValue(key, out var first))
            {
                throw new ArgumentException(
                    $"{where} has the same IdempotencyKey as Recipients[{first}]. Give each recipient its own key.",
                    paramName);
            }

            firstWithKey[key] = index;
            var json = JsonSerializer.Serialize(wire, JsonSerializerHelper.Options);
            members.Add(new BatchMember(index, recipient.To, key, json, Encoding.UTF8.GetByteCount(json)));
        }

        return members;
    }

    readonly record struct BatchMember(int Index, string To, string IdempotencyKey, string Json, int Bytes);

    sealed record BatchChunk(string Body, IReadOnlyList<int> Indices);

    /// <summary>
    /// The recipients of one batch call, split into requests, and what PostHog answered for each recipient.
    /// </summary>
    sealed class BatchRun
    {
        readonly List<BatchMember> _members;
        readonly BatchEmailResult?[] _results;
        readonly HashSet<int> _maybeQueued = [];
        readonly List<EmailWarning> _warnings = [];

        public BatchRun(string empty, List<BatchMember> members, int maxRetries)
        {
            _members = members;
            _results = new BatchEmailResult?[members.Count];
            MaxRetries = maxRetries;
            Chunks = Split(empty);
        }

        public int MaxRetries { get; }

        public List<BatchChunk> Chunks { get; }

        public IReadOnlyList<EmailWarning> Warnings => _warnings;

        List<BatchChunk> Split(string empty)
        {
            var emptyBytes = Encoding.UTF8.GetByteCount(empty);
            var chunks = new List<BatchChunk>();
            var current = new List<BatchMember>();
            var size = emptyBytes;
            foreach (var member in _members)
            {
                if (emptyBytes + member.Bytes > BatchMaxBodyBytes)
                {
                    Set(member.Index, "failed", error: RecipientTooLarge());
                    continue;
                }

                var added = member.Bytes + (current.Count > 0 ? 1 : 0);
                if (current.Count > 0 && (current.Count == BatchMaxRecipients || size + added > BatchMaxBodyBytes))
                {
                    chunks.Add(Chunk(empty, current));
                    current = [];
                    size = emptyBytes;
                    added = member.Bytes;
                }

                current.Add(member);
                size += added;
            }

            if (current.Count > 0)
            {
                chunks.Add(Chunk(empty, current));
            }

            return chunks;
        }

        static BatchChunk Chunk(string empty, List<BatchMember> members)
        {
            var withoutClosingBrackets = empty.Substring(0, empty.Length - "]}".Length);
            return new BatchChunk(
                withoutClosingBrackets + string.Join(",", members.Select(member => member.Json)) + "]}",
                members.Select(member => member.Index).ToList());
        }

        /// <summary>An attempt that may have queued some of the chunk got no usable answer.</summary>
        public void Lost(BatchChunk chunk)
        {
            foreach (var index in chunk.Indices)
            {
                _maybeQueued.Add(index);
                var messageId = _results[index]?.MessageId;
                Set(index, "unconfirmed", messageId, error: Unconfirmed(messageId));
            }
        }

        public void Answer(BatchChunk chunk, Reply reply)
        {
            if (!IsSuccess(reply.Status))
            {
                if ((int)reply.Status >= 500)
                {
                    Lost(chunk);
                }

                throw BatchReplyError(reply, chunk.Indices);
            }

            if (reply.Fields is not { } fields
                || !fields.TryGetProperty("results", out var slots)
                || slots.ValueKind != JsonValueKind.Array
                || slots.GetArrayLength() != chunk.Indices.Count)
            {
                Lost(chunk);
                throw new EmailException(
                    "api_error",
                    "PostHog answered the batch without a result for each recipient.",
                    reply.Status);
            }

            var position = 0;
            foreach (var slot in slots.EnumerateArray())
            {
                Settle(chunk.Indices[position++], slot);
            }

            foreach (var warning in ReadWarnings(fields))
            {
                if (!_warnings.Any(seen => (seen.Code, seen.Message, seen.Fix) == (warning.Code, warning.Message, warning.Fix)))
                {
                    _warnings.Add(warning);
                }
            }
        }

        void Settle(int index, JsonElement slot)
        {
            var messageId = Text(slot, "message_id");
            JsonElement? error = slot.ValueKind == JsonValueKind.Object
                                 && slot.TryGetProperty("error", out var answered)
                                 && answered.ValueKind != JsonValueKind.Null
                ? answered
                : null;
            if (error is null && Text(slot, "status") == "queued" && messageId is not null)
            {
                _maybeQueued.Remove(index);
                var replayed = slot.TryGetProperty("replayed", out var flag) && flag.ValueKind == JsonValueKind.True;
                Set(index, "queued", messageId, replayed);
                return;
            }

            var answer = SlotError(error, messageId);
            if (_maybeQueued.Contains(index))
            {
                var earlier = Current(index);
                Set(index, "unconfirmed", messageId ?? earlier.MessageId, error: earlier.Error, laterError: answer);
            }
            else if (UnconfirmedSlotCodes.Contains(answer.Code))
            {
                _maybeQueued.Add(index);
                Set(index, "unconfirmed", messageId, error: answer);
            }
            else
            {
                Set(index, "failed", messageId, error: answer);
            }
        }

        /// <summary>The request of chunk <paramref name="position"/> failed as a whole, so the batch stops there.</summary>
        public void Stop(int position, EmailException error)
        {
            var later = error.StatusCode is { } status && (int)status < 500 ? error : null;
            foreach (var index in Chunks[position].Indices)
            {
                if (!_maybeQueued.Contains(index))
                {
                    Set(index, "failed", error: error);
                }
                else if (later is not null)
                {
                    var earlier = Current(index);
                    Set(index, "unconfirmed", earlier.MessageId, error: earlier.Error, laterError: later);
                }
            }

            SkipFrom(
                position + 1,
                new EmailException(
                    "not_attempted",
                    "An earlier request of the batch failed, so the client sent nothing for this recipient."));
        }

        /// <summary>The client shut down before chunk <paramref name="position"/> went out.</summary>
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
            foreach (var chunk in Chunks.Skip(position))
            {
                foreach (var index in chunk.Indices)
                {
                    Set(index, "not_attempted", error: error);
                }
            }
        }

        /// <summary>Recipients a lost answer left without a message ID, with their keys, in input order.</summary>
        public List<(int Index, string Key)> LostKeys()
            => _maybeQueued
                .Where(index => Current(index).MessageId is null)
                .OrderBy(index => index)
                .Select(index => (index, _members[index].IdempotencyKey))
                .ToList();

        public void Accepted(int index, EmailStatus status)
        {
            _maybeQueued.Remove(index);
            Set(index, "queued", status.Id);
        }

        public List<BatchEmailResult> Outcome() => _results.Select((_, index) => Current(index)).ToList();

        BatchEmailResult Current(int index)
            => _results[index] ?? throw new InvalidOperationException($"Recipient {index} has no result yet.");

        void Set(
            int index,
            string status,
            string? messageId = null,
            bool replayed = false,
            EmailException? error = null,
            EmailException? laterError = null)
        {
            var member = _members[index];
            _results[index] = new BatchEmailResult
            {
                Index = index,
                To = member.To,
                IdempotencyKey = member.IdempotencyKey,
                Status = status,
                MessageId = messageId,
                Replayed = replayed,
                Error = error,
                LaterError = laterError,
            };
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
            HttpStatusCode? status = answer.TryGetProperty("status", out var number)
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

        static EmailException Unconfirmed(string? messageId) => new(
            "delivery_unavailable",
            "PostHog didn't confirm this email, so it may still go out. Send it again with the same IdempotencyKey "
            + "within 24 hours, so it goes out once.",
            messageId: messageId);

        static EmailException RecipientTooLarge() => new(
            "content_too_large",
            "This recipient's email is larger than one batch request takes, so it wasn't sent.",
            fix: "Send this recipient's email with SendAsync.",
            field: "recipients");
    }

    static EmailException BatchReplyError(Reply reply, IReadOnlyList<int> indices)
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
            field: Text(reply.Fields, "attr") is { } attr ? RecipientsField(attr, indices) : null,
            retryAfter: reply.RetryAfter);
    }

    /// <summary>
    /// Names a field PostHog refused by the recipient's position in the caller's list, not in the request.
    /// </summary>
    static string RecipientsField(string attr, IReadOnlyList<int> indices)
    {
        const string prefix = "recipients__";
        if (!attr.StartsWith(prefix, StringComparison.Ordinal))
        {
            return attr;
        }

        var rest = attr.Substring(prefix.Length);
        var separator = rest.IndexOf("__", StringComparison.Ordinal);
        var position = separator < 0 ? rest : rest.Substring(0, separator);
        if (position.Length == 0 || position.Any(c => c is < '0' or > '9'))
        {
            return attr;
        }

        if (position.Length > indices.Count.ToString(CultureInfo.InvariantCulture).Length)
        {
            return "recipients";
        }

        var inChunk = int.Parse(position, CultureInfo.InvariantCulture);
        if (inChunk >= indices.Count)
        {
            return "recipients";
        }

        return prefix + indices[inChunk].ToString(CultureInfo.InvariantCulture)
               + (separator < 0 ? string.Empty : rest.Substring(separator));
    }
}
