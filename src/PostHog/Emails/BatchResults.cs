namespace PostHog.Emails;

/// <summary>
/// What <see cref="IPostHogEmails.BatchAsync"/> did for each recipient.
/// </summary>
public sealed class SendBatchResult
{
    /// <summary>
    /// One result per recipient, in the order of <see cref="SendBatchOptions.Recipients"/>.
    /// </summary>
    public required IReadOnlyList<BatchEmailResult> Results { get; init; }

    /// <summary>
    /// The warnings PostHog returned about the batch, each once. The client also logs each one.
    /// </summary>
    public IReadOnlyList<EmailWarning> Warnings { get; init; } = [];
}

/// <summary>
/// What happened to one recipient of <see cref="IPostHogEmails.BatchAsync"/>.
/// </summary>
public sealed class BatchEmailResult
{
    /// <summary>
    /// The recipient's position in <see cref="SendBatchOptions.Recipients"/>.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// The recipient's email address.
    /// </summary>
    public required string To { get; init; }

    /// <summary>
    /// The recipient's idempotency key, given or made by the client. Every retry of the batch sent this key.
    /// </summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// <c>queued</c>, <c>failed</c>, <c>unconfirmed</c> or <c>not_attempted</c>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>queued</c>: PostHog accepted the email. Read its status with <see cref="IPostHogEmails.GetAsync"/> to
    /// see whether it went out.</item>
    /// <item><c>failed</c>: PostHog didn't queue the email, and <see cref="Error"/> says why, such as a suppressed
    /// address.</item>
    /// <item><c>unconfirmed</c>: the client can't tell whether PostHog queued the email, such as after a lost answer.
    /// It may still go out. Send it again with the same <see cref="IdempotencyKey"/> within 24 hours, so it goes out
    /// once. <see cref="LaterError"/> holds what a retry in the same call answered.</item>
    /// <item><c>not_attempted</c>: an earlier request of the batch failed, so the client sent nothing for this
    /// recipient.</item>
    /// </list>
    /// </remarks>
    public required string Status { get; init; }

    /// <summary>
    /// The email's message ID, when PostHog named it. Pass it to <see cref="IPostHogEmails.GetAsync"/>.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    /// <c>true</c> when an earlier send with the same key had already queued the email, so no new email was sent.
    /// </summary>
    public bool Replayed { get; init; }

    /// <summary>
    /// Why the email is not <c>queued</c>. For an <c>unconfirmed</c> email, <see cref="EmailException.Code"/> is
    /// <c>delivery_unavailable</c>, <c>idempotency_key_in_flight</c> or <c>api_error</c>. <c>null</c> for a
    /// <c>queued</c> email.
    /// </summary>
    public EmailException? Error { get; init; }

    /// <summary>
    /// For an <c>unconfirmed</c> email, what a later retry in the same call answered about it. It
    /// describes only that answer: the email may still go out.
    /// </summary>
    public EmailException? LaterError { get; init; }
}
