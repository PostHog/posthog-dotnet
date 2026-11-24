namespace PostHog.Emails;

/// <summary>
/// A request of <see cref="IPostHogEmails.BatchAsync"/> failed as a whole, such as a rate limit past its retries, so
/// the batch stopped.
/// </summary>
/// <remarks>
/// The <see cref="EmailException"/> properties describe that request's error, which is also the
/// <see cref="Exception.InnerException"/>. <see cref="Results"/> holds every recipient: those of earlier requests as
/// PostHog answered them, those of the failed request as <c>failed</c>, or <c>unconfirmed</c> where an earlier attempt
/// may have queued them, and the rest as <c>not_attempted</c>. <see cref="EmailException.Code"/> is
/// <c>batch_unavailable</c> when the project can't send batches yet, and <see cref="EmailException.Name"/> is
/// <c>not_found</c> when the template doesn't exist or batch sends are off for the project: send each email the batch
/// didn't queue with <see cref="IPostHogEmails.SendAsync"/> and its recipient's idempotency key.
/// <c>client_closed</c> means the client shut down during the batch.
/// </remarks>
public sealed class EmailBatchException : EmailException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmailBatchException"/> class with the code
    /// <c>unexpected_error</c> and no results.
    /// </summary>
    public EmailBatchException()
        : base("The batch failed unexpectedly.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailBatchException"/> class with the code
    /// <c>unexpected_error</c> and no results.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public EmailBatchException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailBatchException"/> class with the code
    /// <c>unexpected_error</c> and no results.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public EmailBatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal EmailBatchException(
        EmailException error,
        IReadOnlyList<BatchEmailResult> results,
        IReadOnlyList<EmailWarning> warnings)
        : base(
            error.Code,
            error.Message,
            error.StatusCode,
            error.Why,
            error.Fix,
            error.DocsUrl,
            error.Field,
            error.RetryAfter,
            innerException: error)
    {
        Results = results;
        Warnings = warnings;
    }

    /// <summary>
    /// One result per recipient, in the order of <see cref="SendBatchOptions.Recipients"/>.
    /// </summary>
    public IReadOnlyList<BatchEmailResult> Results { get; } = [];

    /// <summary>
    /// The warnings PostHog returned about the requests that went out, each once. The client also logs each one.
    /// </summary>
    public IReadOnlyList<EmailWarning> Warnings { get; } = [];
}
