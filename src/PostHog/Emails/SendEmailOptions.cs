namespace PostHog.Emails;

/// <summary>
/// One email to send with <see cref="IPostHogEmails.SendAsync"/>. Pass a
/// <see cref="SendTemplateEmailOptions"/> to send a template made in PostHog, or a
/// <see cref="SendCodeEmailOptions"/> to send content your code rendered.
/// </summary>
public abstract class SendEmailOptions
{
    private protected SendEmailOptions()
    {
    }

    /// <summary>
    /// One recipient email address.
    /// </summary>
    public required string To { get; init; }

    /// <summary>
    /// The recipient's distinct ID in your product. The email's events go to this person.
    /// Without it, they go to a person whose distinct ID is the email address.
    /// </summary>
    public string? DistinctId { get; init; }

    /// <summary>
    /// 1 to 255 visible ASCII characters, without spaces.
    /// </summary>
    /// <remarks>
    /// A send with a key that PostHog already accepted queues nothing and returns the first send's result.
    /// The same key with other options sends nothing and fails with <c>idempotency_key_reused</c>. Without a key,
    /// the client makes a UUID that covers its own retries. Pass a key derived from what the email is for, such as
    /// <c>welcome:user_123</c>, to cover retries outside this call too, such as a job runner's.
    /// </remarks>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Overrides <see cref="PostHogOptions.EmailRequestMaxRetries"/> for this send. <c>0</c> turns retries off.
    /// </summary>
    public int? MaxRetries { get; init; }
}

/// <summary>
/// A send of an email template made transactional in PostHog, named by its ID. The template sets its own sender,
/// subject and content.
/// </summary>
public sealed class SendTemplateEmailOptions : SendEmailOptions
{
    /// <summary>
    /// The ID of an email template made transactional in PostHog.
    /// </summary>
    public required Guid TemplateId { get; init; }

    /// <summary>
    /// Values for the template's <c>{{ variables.* }}</c> placeholders.
    /// </summary>
    public IReadOnlyDictionary<string, object>? TemplateData { get; init; }
}

/// <summary>
/// A send of email content your code rendered, named by a key. PostHog sends it as is and never stores it.
/// </summary>
/// <remarks>
/// Needs <see cref="Html"/>, <see cref="Text"/> or both. To send a React email, render it to HTML in your app first
/// and pass it as <see cref="Html"/>.
/// </remarks>
public sealed class SendCodeEmailOptions : SendEmailOptions
{
    /// <summary>
    /// Names the email, such as <c>password-reset</c>, so its metrics, logs and status group under it in PostHog.
    /// Letters, digits, hyphens and underscores, at most 400.
    /// </summary>
    public required string TemplateKey { get; init; }

    /// <summary>
    /// The subject line.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// The HTML body.
    /// </summary>
    public string? Html { get; init; }

    /// <summary>
    /// The plain-text body. Without it, PostHog derives it from <see cref="Html"/>.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// A verified sender, such as <c>Acme &lt;security@example.com&gt;</c>. Without it, the project's default sender.
    /// </summary>
    public string? From { get; init; }

    /// <summary>
    /// The addresses replies go to.
    /// </summary>
    public IReadOnlyList<string>? ReplyTo { get; init; }

    /// <summary>
    /// The message category to send the email in. Without it, PostHog sends the email in the category of the
    /// template that <see cref="TemplateKey"/> names, or as transactional with no category when no template has
    /// that key. <see cref="MessageCategory.Transactional"/> sends it as transactional with no category.
    /// </summary>
    /// <remarks>
    /// A marketing category's email honors the recipient's opt-outs. Its <see cref="Html"/> needs a link whose
    /// <c>href</c> is <c>{{ unsubscribe_url }}</c>, and its <see cref="Text"/>, when it has no <see cref="Html"/>, needs
    /// the <c>{{ unsubscribe_url }}</c> placeholder. PostHog replaces it with the recipient's preferences link.
    /// PostHog refuses the send with status 400 and an <see cref="EmailException.Code"/> of <c>category_mismatch</c>
    /// when the key names a category of the other type, or when <see cref="TemplateKey"/> names a template in another
    /// category, including <see cref="MessageCategory.Transactional"/> for a template in a category;
    /// <c>too_many_categories</c> when it would create a category past the project's limit;
    /// <c>unsubscribe_link_required</c> without that link or placeholder; or <c>copies_not_allowed</c> when a
    /// marketing email has copies. It refuses it with status 422 and <c>recipient_opted_out</c> when the recipient
    /// opted out of the category or of all marketing.
    /// </remarks>
    public MessageCategory? Category { get; init; }

    /// <summary>
    /// Whether PostHog captures the email's engagement events (sent, delivered, opened, clicked, bounced) as events
    /// of the recipient. (Default: <c>true</c>)
    /// </summary>
    /// <remarks>
    /// A project whose admin turned engagement capture off captures nothing, whatever the send asks. A template made
    /// in PostHog follows the project's setting, so <see cref="SendTemplateEmailOptions"/> doesn't take it.
    /// </remarks>
    public bool CaptureEngagement { get; init; } = true;
}
