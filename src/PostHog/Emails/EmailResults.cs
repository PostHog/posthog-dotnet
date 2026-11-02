namespace PostHog.Emails;

/// <summary>
/// The email PostHog queued. A result means PostHog queued the email, not that it was delivered.
/// </summary>
public sealed class SendEmailResult
{
    /// <summary>
    /// The queued email's message ID. Pass it to <see cref="IPostHogEmails.GetAsync"/>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// <c>true</c> when PostHog had already accepted this idempotency key and returned the first send's result.
    /// </summary>
    public bool Replayed { get; init; }

    /// <summary>
    /// The warnings PostHog returned with the queued email. The client also logs each one.
    /// </summary>
    public IReadOnlyList<EmailWarning> Warnings { get; init; } = [];
}

/// <summary>
/// A warning PostHog returned with a queued email.
/// </summary>
public sealed class EmailWarning
{
    /// <summary>
    /// PostHog's warning code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// What the warning means.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// How to fix it, when PostHog said.
    /// </summary>
    public string? Fix { get; init; }
}

/// <summary>
/// What happened to an email that <see cref="IPostHogEmails.SendAsync"/> queued.
/// </summary>
public sealed class EmailStatus
{
    /// <summary>
    /// The message ID that <see cref="IPostHogEmails.SendAsync"/> returned.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// One of <c>queued</c>, <c>sent</c>, <c>failed</c>, <c>delivered</c>, <c>bounced</c> or <c>complained</c>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>queued</c>: the email waits to go out.</item>
    /// <item><c>sent</c>: PostHog handed the email to the email provider.</item>
    /// <item><c>failed</c>: the email was not sent, and <see cref="Reason"/> says why.</item>
    /// <item><c>delivered</c>: the recipient's mail server accepted the email.</item>
    /// <item><c>bounced</c> and <c>complained</c>: the recipient's mail provider reported them after delivery.</item>
    /// </list>
    /// Later versions of PostHog can answer values this list does not have yet.
    /// </remarks>
    public required string Status { get; init; }

    /// <summary>
    /// When PostHog queued the email.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the email reached its current status.
    /// </summary>
    public required DateTimeOffset StatusAt { get; init; }

    /// <summary>
    /// Why a failed email was not sent, such as <c>provider_error</c> or <c>recipient_suppressed</c>, or the bounce
    /// type of a bounced email: <c>hard</c>, <c>soft</c> or <c>unknown</c>. <c>null</c> otherwise.
    /// </summary>
    public string? Reason { get; init; }
}
