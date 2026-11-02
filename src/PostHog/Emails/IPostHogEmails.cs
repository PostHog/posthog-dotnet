namespace PostHog.Emails;

/// <summary>
/// Sends transactional email, from email templates made in PostHog or from content your code rendered, and reads
/// the status of each email it sent. Reach it through <see cref="IPostHogClient.Emails"/>.
/// </summary>
/// <remarks>
/// Needs <see cref="PostHogOptions.SecretKey"/> set to a project secret API key (<c>phs_...</c>) with the
/// <c>hog_flow:write</c> scope to send, or <c>hog_flow:read</c> to read a status. Requests go to the PostHog app
/// host: PostHog Cloud hosts map to theirs, and any other <see cref="PostHogOptions.HostUrl"/> needs
/// <see cref="PostHogOptions.AppHost"/>.
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
