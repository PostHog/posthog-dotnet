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
/// <para>
/// A PostHog version that doesn't take <see cref="Cc"/> or <see cref="Bcc"/> yet refuses the send with a
/// <c>validation_error</c> whose <see cref="EmailException.Field"/> names the field. A project that can't send files
/// yet gets <c>field_not_accepted</c>. A PostHog version without file uploads, or a project that can't send email
/// from code, answers <c>not_found</c> on <c>attachments</c> with PostHog's reason. None of them sends the email.
/// </para>
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
    /// Bare addresses, such as <c>finance@example.com</c>, that get a visible copy. At most 10.
    /// </summary>
    public IReadOnlyList<string>? Cc { get; init; }

    /// <summary>
    /// Bare addresses that get a copy the other recipients never see. At most 10.
    /// </summary>
    public IReadOnlyList<string>? Bcc { get; init; }

    /// <summary>
    /// Files sent with the email: at most 10, and at most 40 MB for the whole email after Base64 encoding, which
    /// leaves about 29 MB for the files.
    /// </summary>
    /// <remarks>
    /// The client checks both limits before any request. It then asks PostHog which files it lacks, uploads each of
    /// those straight to PostHog's file storage, and sends the email only once every upload succeeded. A file this
    /// project sent before is usually still stored and isn't uploaded again. Uploads go to the storage host PostHog
    /// names, not to <see cref="PostHogOptions.AppHost"/>, never carry the secret key, and never follow a redirect:
    /// a redirect fails the send with <c>attachment_upload_failed</c>.
    /// </remarks>
    public IReadOnlyList<EmailAttachment>? Attachments { get; init; }

    /// <summary>
    /// Whether the email is sensitive: set <c>true</c> when it carries login links, reset tokens or codes. A sensitive
    /// email keeps its links unchanged unless <see cref="Tracking"/> turns clicks on, leaves its subject off
    /// engagement events, and keeps its content out of logs.
    /// </summary>
    /// <remarks>
    /// Left unset, the email takes the setting of the template made in PostHog whose key matches
    /// <see cref="TemplateKey"/>, and is sensitive when no template matches. The send includes it only when you set it.
    /// Needs PostHog's API support for <c>sensitive</c>, which is rolling out; until then PostHog refuses a send that
    /// sets it. A template made in PostHog carries its own setting, so <see cref="SendTemplateEmailOptions"/> doesn't
    /// take it.
    /// </remarks>
    public bool? Sensitive { get; init; }

    /// <summary>
    /// What PostHog tracks for the email: opens, clicks and engagement events. Left unset, PostHog counts opens and
    /// captures engagement events, and tracks clicks only when the email isn't <see cref="Sensitive"/>.
    /// </summary>
    /// <remarks>
    /// Set <see cref="EmailTracking.All"/> (or <c>true</c>) to turn on every kind, <see cref="EmailTracking.None"/>
    /// (or <c>false</c>) to turn them all off, or an <see cref="EmailTracking"/> with <see cref="EmailTracking.Opens"/>,
    /// <see cref="EmailTracking.Clicks"/> or <see cref="EmailTracking.Engagement"/> to set one kind. Left unset, the
    /// send doesn't include it. Needs PostHog's API support for <c>tracking</c>, which is rolling out; until then
    /// PostHog refuses a send that sets it. A template made in PostHog carries its own tracking setting, so
    /// <see cref="SendTemplateEmailOptions"/> doesn't take it.
    /// </remarks>
    public EmailTracking? Tracking { get; init; }

    /// <summary>
    /// The message category to send the email in. Without it, PostHog sends the email in the transactional category
    /// of the template that <see cref="TemplateKey"/> names (when code-send categories are enabled for the project),
    /// or as transactional with no category when no template has that key. <see cref="MessageCategory.Transactional"/>
    /// sends it as transactional with no category.
    /// </summary>
    /// <remarks>
    /// Requires a PostHog server with code-send category support: requests that reach the send endpoint on older
    /// servers refuse <c>category</c> with HTTP 400 naming the unknown field, while a send with
    /// <see cref="Attachments"/> to a server without the upload route stops at HTTP 404 <c>not_found</c> on
    /// <c>attachments</c> before submitting the email body. On a compatible server,
    /// <see cref="MessageCategory.Transactional"/> works even when code-send categories are disabled for the project;
    /// enabling code-send categories for the project gates named <see cref="MessageCategory"/> objects (otherwise
    /// PostHog refuses them with <c>categories_not_enabled</c>) and template-category inheritance when
    /// <see cref="Category"/> is omitted.
    /// An email in a marketing category honors the recipient's opt-outs. Its <see cref="Html"/> needs a clickable
    /// <c>&lt;a href="{{ unsubscribe_url }}"&gt;</c> link, and its <see cref="Text"/>, when it has no
    /// <see cref="Html"/>, needs the <c>{{ unsubscribe_url }}</c> placeholder. PostHog replaces it with the
    /// recipient's preferences link.
    /// Among template sendability and category checks, a named <see cref="MessageCategory"/> fails with
    /// <c>categories_not_enabled</c> first when code-send categories are disabled for the project. Otherwise, when
    /// <see cref="TemplateKey"/> resolves to a saved template, PostHog refuses a sensitive template when
    /// <see cref="Sensitive"/> is <c>false</c> (<c>sensitive_template</c>) first, then refuses a saved push template
    /// with <c>template_not_sendable</c>; for a saved email template, PostHog checks whether it is enabled for
    /// transactional sends (<c>template_not_transactional</c> otherwise) before checking whether it is in a marketing
    /// category (<c>marketing_template</c>) or comparing categories.
    /// PostHog refuses the send with status 400 and an <see cref="EmailException.Code"/> of <c>category_mismatch</c>
    /// when the key names a category of the other type, or when <see cref="TemplateKey"/> names a template in another
    /// category, including <see cref="MessageCategory.Transactional"/> for a template in a category;
    /// <c>too_many_categories</c> when it would create a new category or restore an archived category past the
    /// project's active-category limit; and, for an email in a marketing category whose <see cref="TemplateKey"/>
    /// names no template, <c>unsubscribe_link_required</c> without that link or placeholder, <c>copies_not_allowed</c>
    /// when it has <see cref="Cc"/> or <see cref="Bcc"/>, or <c>workflow_can_wait</c> on a verified sender (preceded by
    /// <c>workflow_shape_not_supported</c> if an existing code-send workflow for <see cref="TemplateKey"/> has an
    /// unsupported shape, such as an extra reachable step) or <c>sandbox_fields_unsupported</c> on <c>attachments</c>
    /// when using the PostHog test sender and the email has <see cref="Attachments"/> (marketing code sends cannot
    /// carry attachments because marketing sending limits can hold delivery; the client first asks PostHog which
    /// attachments it lacks, uploads only those, and prevents the send on a terminal upload or storage error after
    /// retries are exhausted or when the error is not retryable, before submitting the send for PostHog to refuse). It
    /// refuses the send with status 422 and <c>recipient_opted_out</c> when the recipient opted out of the category or
    /// of all marketing (if preference lookups fail, in either synchronous or async admission mode, or if async
    /// admission checks exceed the admission budget, PostHog may still accept the send and defer the preference check
    /// to the delivery worker).
    /// </remarks>
    public MessageCategory? Category { get; init; }

    /// <summary>
    /// Whether PostHog captures the email's engagement events (sent, delivered, opened, clicked, bounced) as events
    /// of the recipient. (Default: <c>true</c>)
    /// </summary>
    /// <remarks>
    /// Deprecated: set <see cref="EmailTracking.Engagement"/> on <see cref="Tracking"/> instead. It still works, but a
    /// send can't set both. The send includes it only when you set it. A
    /// project whose admin turned engagement capture off captures nothing, whatever the send asks.
    /// </remarks>
    [Obsolete("Use Tracking = new EmailTracking { Engagement = false } instead. A send can't set both.")]
    public bool CaptureEngagement
    {
        get => CaptureEngagementIfSet ?? true;
        init => CaptureEngagementIfSet = value;
    }

    internal bool? CaptureEngagementIfSet { get; private init; }
}

/// <summary>
/// A file sent with a <see cref="SendCodeEmailOptions"/> email.
/// </summary>
public sealed class EmailAttachment
{
    /// <summary>
    /// The file name the recipient sees, such as <c>invoice.pdf</c>. No path.
    /// </summary>
    public required string Filename { get; init; }

    /// <summary>
    /// The file's bytes. The client names the file by their SHA-256 and uploads them when PostHog doesn't have them
    /// yet. PostHog can't fetch a file from a path or URL, so read the file first. An empty file throws
    /// <see cref="ArgumentException"/> before any request.
    /// </summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>
    /// The MIME type, such as <c>application/pdf</c>. Without it, the type follows from <see cref="Filename"/>.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Shows the file inline: with <c>logo</c> here, reference it in the HTML as <c>&lt;img src="cid:logo"&gt;</c>.
    /// </summary>
    public string? ContentId { get; init; }
}
