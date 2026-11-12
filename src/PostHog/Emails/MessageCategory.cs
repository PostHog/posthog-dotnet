namespace PostHog.Emails;

/// <summary>
/// The message category an email from code is sent in, set on <see cref="SendCodeEmailOptions.Category"/>. Pass
/// <see cref="Transactional"/>, or define a category of the project in code.
/// </summary>
/// <remarks>
/// PostHog creates the project's category by <see cref="Key"/> when it accepts a send with a key the project doesn't
/// have, and restores a deleted one. <see cref="Name"/> and <see cref="Description"/> apply only when the send creates
/// the category: a send never renames a category or changes its description, so change them later in PostHog. A key
/// that exists keeps its type, so a send with another <see cref="Type"/> fails with <c>category_mismatch</c>. A send
/// whose <see cref="SendCodeEmailOptions.TemplateKey"/> names a template sends in the template's category instead, and
/// creates no category.
/// </remarks>
public sealed class MessageCategory
{
    /// <summary>
    /// Sends the email as transactional with no category. Unlike leaving <see cref="SendCodeEmailOptions.Category"/>
    /// out, it doesn't follow the category of the template that <see cref="SendCodeEmailOptions.TemplateKey"/> names:
    /// PostHog refuses it with <c>category_mismatch</c> for a template in a category.
    /// </summary>
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
