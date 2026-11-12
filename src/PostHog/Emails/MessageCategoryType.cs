namespace PostHog.Emails;

/// <summary>
/// The kind of email a <see cref="MessageCategory"/> holds.
/// </summary>
public enum MessageCategoryType
{
    /// <summary>
    /// Marketing email: PostHog honors the recipient's opt-outs and adds unsubscribe headers.
    /// </summary>
    Marketing,

    /// <summary>
    /// Transactional email, such as a receipt or a password reset. Marketing opt-outs don't apply to it.
    /// </summary>
    Transactional,
}
