namespace PostHog.Emails;

/// <summary>
/// Sends transactional email, from email templates made in PostHog or from content your code rendered, and reads
/// the status of each email it sent. Reach it through <see cref="PostHogClient.Emails"/>, or inject it when you
/// register PostHog with <c>AddPostHog</c>.
/// </summary>
/// <remarks>
/// Needs <see cref="PostHogOptions.SecretKey"/> set to a project secret API key (<c>phs_...</c>) with the
/// <c>transactional_email:write</c> scope to send, or <c>transactional_email:read</c> to read a status. Keys made
/// with <c>hog_flow:write</c> still work. Requests go to the PostHog app host: PostHog Cloud hosts map to theirs,
/// and any other <see cref="PostHogOptions.HostUrl"/> needs <see cref="PostHogOptions.AppHost"/>.
/// </remarks>
public interface IPostHogEmails
{
    /// <summary>
    /// Queues one email, from a template made in PostHog or from content your code rendered.
    /// </summary>
    /// <remarks>
    /// Network errors, rate limits, an idempotency key still in flight and an unavailable service are retried with
    /// the same idempotency key, so a retry never sends twice.
    /// </remarks>
    /// <param name="email">
    /// A <see cref="SendTemplateEmailOptions"/> or a <see cref="SendCodeEmailOptions"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The queued email. It means PostHog queued the email, not that it was delivered.</returns>
    /// <exception cref="ArgumentException">An option is invalid. No request was made.</exception>
    /// <exception cref="EmailException">PostHog did not queue the email, or the client can't tell whether it did.</exception>
    Task<SendEmailResult> SendAsync(SendEmailOptions email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues the same email for many recipients, one email each, from a template made in PostHog, with
    /// <see cref="BatchRecipient.TemplateData"/> per recipient, or from content your code rendered.
    /// </summary>
    /// <remarks>
    /// The client sends the recipients in requests of at most 100 recipients and 4 MiB of JSON, one after another, and
    /// waits up to 45 seconds for each answer, or <see cref="PostHogOptions.EmailRequestTimeout"/> when that is
    /// longer. The <see cref="HttpClient.Timeout"/> of the <see cref="HttpClient"/> the client sends through still
    /// ends an attempt when it is shorter, so give that <see cref="HttpClient"/> a timeout of at least 45 seconds. A
    /// recipient too large for any request alone is <c>failed</c> with <c>content_too_large</c>, and the rest still go
    /// out.
    /// <para>
    /// Each recipient has an idempotency key, its <see cref="BatchRecipient.IdempotencyKey"/> or one the client makes,
    /// that every retry reuses, so a retry never queues an email twice. PostHog replays a key for 24 hours after the
    /// first request that used it: a batch sent again within that window with the same keys queues no second email
    /// for a recipient PostHog already accepted. Network errors, rate limits, a key still in flight and an unavailable
    /// service are retried for each request. When a request fails after the client may have queued some of its
    /// emails, the client reads each of those emails by its key before it throws.
    /// </para>
    /// </remarks>
    /// <param name="batch">
    /// A <see cref="SendTemplateBatchOptions"/> or a <see cref="SendCodeBatchOptions"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>
    /// A result per recipient, in the order of <see cref="SendBatchOptions.Recipients"/>. Check each
    /// <see cref="BatchEmailResult.Status"/>: <c>queued</c>, <c>failed</c> or <c>unconfirmed</c>. Send an
    /// <c>unconfirmed</c> email again with the same key within 24 hours, so it goes out once.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// An option is invalid, such as two recipients with the same idempotency key. No request was made.
    /// </exception>
    /// <exception cref="EmailBatchException">
    /// A request failed as a whole, so the batch stopped. <see cref="EmailBatchException.Results"/> holds every
    /// recipient, those not sent as <c>not_attempted</c>.
    /// </exception>
    /// <exception cref="EmailException">The client sent nothing, such as when it is disabled.</exception>
    Task<SendBatchResult> BatchAsync(SendBatchOptions batch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the status of an email that <see cref="SendAsync"/> queued, for 30 days after the send.
    /// </summary>
    /// <remarks>
    /// The error code is <c>not_found</c> when no email of this project has the ID, when the email is older than
    /// 30 days, or for a few seconds after the send. Network errors, rate limits and an unavailable service are
    /// retried.
    /// </remarks>
    /// <param name="messageId">The message ID that <see cref="SendAsync"/> returned.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The email's status.</returns>
    /// <exception cref="ArgumentException"><paramref name="messageId"/> is not a message ID. No request was made.</exception>
    /// <exception cref="EmailException">PostHog did not answer with the email's status.</exception>
    Task<EmailStatus> GetAsync(string messageId, CancellationToken cancellationToken = default);
}
