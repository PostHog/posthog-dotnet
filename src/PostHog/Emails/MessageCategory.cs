namespace PostHog.Emails;

/// <summary>
/// The message category an email from code is sent in, set on <see cref="SendCodeEmailOptions.Category"/>. Pass
/// <see cref="Transactional"/>, or define a category of the project in code.
/// </summary>
/// <remarks>
/// When <see cref="SendCodeEmailOptions.TemplateKey"/> resolves to no saved template, PostHog creates the project's
/// category by <see cref="Key"/> (or restores an archived one) after checking that a marketing send carries the
/// <c>{{ unsubscribe_url }}</c> placeholder and no copies and after sender selection succeeds, before delivery checks
/// run. Because whether the HTML has a clickable unsubscribe link (<c>unsubscribe_link_required</c>) is checked later
/// at delivery, a send that fails there, or fails with <c>recipient_opted_out</c>, still leaves the category created or
/// restored, so retrying with fixed HTML and different metadata keeps the original <see cref="Name"/> and
/// <see cref="Description"/>. Likewise, on a verified sender, a marketing send with a new category and an attachment
/// creates or restores the category before <c>workflow_can_wait</c> (or <c>workflow_shape_not_supported</c>) comes back
/// (before any opt-out check); on the PostHog test sender, <c>sandbox_fields_unsupported</c> returns before category
/// resolution, so no category is created or restored. Both creating a new category and restoring an
/// archived one count against the project's active-category limit (<c>too_many_categories</c>). <see cref="Name"/>
/// and <see cref="Description"/> apply only when the send creates the category: a send never renames a category or
/// changes its description, so change them later in PostHog. A key that exists keeps its type, so a send with another
/// <see cref="Type"/> fails with <c>category_mismatch</c>. When <see cref="SendCodeEmailOptions.TemplateKey"/> names
/// a saved template, the send uses that template's transactional category without creating a category or restoring an
/// archived one: omitting <see cref="SendCodeEmailOptions.Category"/> follows it when code-send categories are enabled
/// for the project, and passing an explicit <see cref="SendCodeEmailOptions.Category"/> must match it or PostHog
/// refuses the send with <c>category_mismatch</c>. With code-send categories enabled, when
/// <see cref="SendCodeEmailOptions.TemplateKey"/> resolves to a saved template, PostHog refuses a sensitive template
/// when <see cref="SendCodeEmailOptions.Sensitive"/> is <c>false</c> (<c>sensitive_template</c>) first, then refuses a
/// saved push template with <c>template_not_sendable</c>; for a saved email template, PostHog checks whether it is
/// enabled for transactional sends (<c>template_not_transactional</c> otherwise) before refusing an otherwise sendable
/// email template in a marketing category with <c>marketing_template</c> or comparing categories.
/// </remarks>
public sealed class MessageCategory
{
    /// <summary>
    /// Sends the email as transactional with no category. Unlike leaving <see cref="SendCodeEmailOptions.Category"/>
    /// out, it doesn't follow the category of the template that <see cref="SendCodeEmailOptions.TemplateKey"/> names:
    /// PostHog refuses it with <c>category_mismatch</c> for a template in a category.
    /// </summary>
    /// <remarks>
    /// Pass <see cref="Transactional"/> directly rather than constructing
    /// <c>new MessageCategory { Key = "transactional", ... }</c>, which sends a named category object instead of the
    /// built-in <c>"transactional"</c> value.
    /// </remarks>
    public static MessageCategory Transactional { get; } = new()
    {
        Key = "transactional",
        Type = MessageCategoryType.Transactional,
        Name = "Transactional",
    };

    /// <summary>
    /// Names the category in the project, such as <c>newsletter</c>. Recipients' opt-outs and preferences use it.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Whether emails in the category are marketing or transactional.
    /// </summary>
    public required MessageCategoryType Type { get; init; }

    /// <summary>
    /// The name recipients see on their preferences page, such as <c>Newsletter</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// What the category's emails are about, such as <c>Monthly product news</c>. It applies only when the send
    /// creates the category.
    /// </summary>
    public string? Description { get; init; }
}
