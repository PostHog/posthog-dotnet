namespace PostHog.Emails;

/// <summary>
/// The caller cancelled <see cref="IPostHogEmails.BatchAsync"/> after a request of the batch went out, so PostHog may
/// still send its emails.
/// </summary>
/// <remarks>
/// <see cref="Results"/> holds every recipient with the idempotency key the batch used, <c>unconfirmed</c> unless
/// PostHog already answered for it. Send the <c>unconfirmed</c> recipients again with those keys within 24 hours of
/// each key's first use, so each email goes out once. A batch cancelled before any request went out throws a plain
/// <see cref="OperationCanceledException"/> instead.
/// </remarks>
public sealed class EmailBatchCanceledException : OperationCanceledException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmailBatchCanceledException"/> class with no results.
    /// </summary>
    public EmailBatchCanceledException()
        : base("The batch was cancelled.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailBatchCanceledException"/> class with no results.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public EmailBatchCanceledException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailBatchCanceledException"/> class with no results.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public EmailBatchCanceledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal EmailBatchCanceledException(
        OperationCanceledException cancel,
        IReadOnlyList<BatchEmailResult> results,
        IReadOnlyList<EmailWarning> warnings,
        CancellationToken cancellationToken)
        : base(
            "The batch was cancelled after its request went out, so PostHog may still send these emails. Send the "
            + "unconfirmed recipients again with the IdempotencyKey each result holds, within 24 hours of the key's "
            + "first use, so each email goes out once.",
            cancel,
            cancellationToken)
    {
        Results = results;
        Warnings = warnings;
    }

    /// <summary>
    /// One result per recipient, in the order of <see cref="SendBatchOptions.Recipients"/>.
    /// </summary>
    public IReadOnlyList<BatchEmailResult> Results { get; } = [];

    /// <summary>
    /// The warnings PostHog returned about the batch, each once. The client also logs each one.
    /// </summary>
    public IReadOnlyList<EmailWarning> Warnings { get; } = [];
}
