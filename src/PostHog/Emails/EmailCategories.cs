namespace PostHog.Emails;

/// <summary>
/// Built-in values for <see cref="SendCodeEmailOptions.Category"/>.
/// </summary>
public static class EmailCategories
{
    /// <summary>
    /// Sends the email as transactional, the same as leaving <see cref="SendCodeEmailOptions.Category"/> out.
    /// </summary>
    public const string Transactional = "transactional";
}
