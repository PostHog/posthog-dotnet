namespace PostHog.Emails;

/// <summary>
/// The request of <see cref="IPostHogEmails.BatchAsync"/> failed as a whole, such as a rate limit past its retries or
/// a batch PostHog refuses as too large.
/// </summary>
/// <remarks>
/// The <see cref="EmailException"/> properties describe the request's error, which is also the
/// <see cref="Exception.InnerException"/>. <see cref="Results"/> holds every recipient: <c>unconfirmed</c> when the
/// answer to this or an earlier attempt was lost or PostHog still holds the request, so the email may still go out,
/// and <c>failed</c> otherwise.
/// <see cref="EmailException.Code"/> is <c>content_too_large</c> with HTTP 413 when the batch is larger than PostHog
/// takes in one request: split the recipients into smaller batches. With HTTP 400 it means the shared content or
/// template data is too large, and <see cref="EmailException.Fix"/> says what to shrink. <see cref="EmailException.Code"/> is
/// <c>batch_unavailable</c> when the project can't send batches yet, and <see cref="EmailException.Name"/> is
/// <c>not_found</c> when the template doesn't exist or batch sends are off for the project: send each email the batch
/// didn't queue with <see cref="IPostHogEmails.SendAsync"/> and its recipient's idempotency key.
/// <c>client_closed</c> means the client shut down before a retry.
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
    /// The warnings PostHog returned about the batch, each once. The client also logs each one.
    /// </summary>
    public IReadOnlyList<EmailWarning> Warnings { get; } = [];
}
