namespace PostHog.Emails;

/// <summary>
/// The message category an email from code is sent in, set on <see cref="SendCodeEmailOptions.Category"/>. Pass
/// <see cref="Transactional"/>, or define a category of the project in code.
/// </summary>
/// <remarks>
/// PostHog creates or updates the project's category by <see cref="Key"/> when it accepts the send: an unknown key
/// creates it, and a changed <see cref="Name"/> or <see cref="Description"/> updates it. A key that exists keeps its
/// type, so a send with another <see cref="Type"/> fails with <c>category_mismatch</c>.
/// </remarks>
public sealed class MessageCategory
{
    /// <summary>
    /// Sends the email as transactional with no category, the same as leaving
    /// <see cref="SendCodeEmailOptions.Category"/> out.
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
    /// What the category's emails are about, such as <c>Monthly product news</c>.
    /// </summary>
    public string? Description { get; init; }
}
