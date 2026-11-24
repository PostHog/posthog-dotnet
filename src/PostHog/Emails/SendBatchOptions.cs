namespace PostHog.Emails;

/// <summary>
/// One email for many recipients, sent with <see cref="IPostHogEmails.BatchAsync"/>, one email each. Pass a
/// <see cref="SendTemplateBatchOptions"/> to send a template made in PostHog, or a <see cref="SendCodeBatchOptions"/>
/// to send content your code rendered.
/// </summary>
/// <remarks>
/// A batch takes no Cc, Bcc, Attachments or ScheduledAt: send such an email with <see cref="IPostHogEmails.SendAsync"/>.
/// </remarks>
public abstract class SendBatchOptions
{
    private protected SendBatchOptions()
    {
    }

    /// <summary>
    /// 1 or more recipients, each sent its own email. The client sends at most 100 recipients, and at most 4 MiB of
    /// JSON, per request.
    /// </summary>
    public required IReadOnlyList<BatchRecipient> Recipients { get; init; }

    /// <summary>
    /// Overrides <see cref="PostHogOptions.EmailRequestMaxRetries"/> for each request of this batch. <c>0</c> turns
    /// retries off.
    /// </summary>
    public int? MaxRetries { get; init; }
}

/// <summary>
/// A batch of an email template made transactional in PostHog, named by its ID. The template sets its own sender,
/// subject and content.
/// </summary>
public sealed class SendTemplateBatchOptions : SendBatchOptions
{
    /// <summary>
    /// The ID of an email template made transactional in PostHog.
    /// </summary>
    public required Guid TemplateId { get; init; }

    /// <summary>
    /// Values for the template's <c>{{ variables.* }}</c> placeholders, shared by every recipient. A recipient's
    /// <see cref="BatchRecipient.TemplateData"/> is merged over them.
    /// </summary>
    public IReadOnlyDictionary<string, object>? TemplateData { get; init; }
}

/// <summary>
/// A batch of email content your code rendered, the same for every recipient, named by a key. PostHog sends it as is.
/// </summary>
/// <remarks>
/// Needs <see cref="Html"/>, <see cref="Text"/> or both. A recipient can't carry
/// <see cref="BatchRecipient.TemplateData"/>: send personalized content with a <see cref="SendTemplateBatchOptions"/>
/// batch, or with <see cref="IPostHogEmails.SendAsync"/>.
/// </remarks>
public sealed class SendCodeBatchOptions : SendBatchOptions
{
    /// <summary>
    /// Names the email, such as <c>launch-news</c>, so its metrics, logs and status group under it in PostHog.
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
    /// A verified sender, such as <c>Acme &lt;news@example.com&gt;</c>. Without it, the project's default sender.
    /// </summary>
    public string? From { get; init; }

    /// <summary>
    /// The addresses replies go to.
    /// </summary>
    public IReadOnlyList<string>? ReplyTo { get; init; }

    /// <summary>
    /// Whether the email is sensitive, as <see cref="SendCodeEmailOptions.Sensitive"/> describes. The batch includes
    /// it only when you set it.
    /// </summary>
    public bool? Sensitive { get; init; }

    /// <summary>
    /// What PostHog tracks for the email, as <see cref="SendCodeEmailOptions.Tracking"/> describes. The batch includes
    /// it only when you set it.
    /// </summary>
    public EmailTracking? Tracking { get; init; }
}

/// <summary>
/// One recipient of <see cref="IPostHogEmails.BatchAsync"/>, who gets their own email.
/// </summary>
public sealed class BatchRecipient
{
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
    /// 1 to 255 visible ASCII characters, without spaces, and a different one for each recipient of the batch.
    /// </summary>
    /// <remarks>
    /// Without it, the client makes a UUID that every retry within this call reuses. A second call makes new keys, so
    /// it can send the email again. Pass a key you store, such as <c>digest:2026-11-23:user_123</c>, when a batch may
    /// be cut off and run again: a rerun within 24 hours of the first request that passes the same key replays the
    /// email that went out instead of sending it twice. A single send and a batch recipient with the same key and the
    /// same email replay each other. Never reuse a key for another email: PostHog may answer with the earlier email's
    /// result and send nothing new.
    /// </remarks>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Only with <see cref="SendTemplateBatchOptions"/>: values for this recipient, merged over the batch's
    /// <see cref="SendTemplateBatchOptions.TemplateData"/>. A top-level key set here replaces the same key there.
    /// </summary>
    public IReadOnlyDictionary<string, object>? TemplateData { get; init; }
}
