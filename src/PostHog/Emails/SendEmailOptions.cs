namespace PostHog.Emails;

/// <summary>
/// One transactional email to send with <see cref="IPostHogEmails.SendAsync"/>. Pass a
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
/// yet gets <c>field_not_accepted</c>, and a PostHog version without file uploads answers <c>not_found</c>. None of
/// them sends the email.
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
    /// names, not to <see cref="PostHogOptions.AppHost"/>, and never carry the secret key.
    /// </remarks>
    public IReadOnlyList<EmailAttachment>? Attachments { get; init; }

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
    /// yet. PostHog can't fetch a file from a path or URL, so read the file first.
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
