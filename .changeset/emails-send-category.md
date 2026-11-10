---
"PostHog": minor
---

`SendCodeEmailOptions.Category` sends an email from code in a message category that you define in code with a `MessageCategory`: its `Key`, `Type` (`MessageCategoryType.Marketing` or `Transactional`), `Name` and an optional `Description`. PostHog creates the category by its key, or updates its name and description. A send whose `TemplateKey` names a template sends in the template's category instead, and creates or updates none. Leave it out to send transactional email with no category, as before, or to send in the category of the template that `TemplateKey` names. `MessageCategory.Transactional` sends transactional email with no category, so PostHog refuses it with `category_mismatch` for a template in a category. A marketing email honors the recipient's opt-outs and needs a `{{ unsubscribe_url }}` placeholder in its content.

```csharp
await posthog.Emails.SendAsync(new SendCodeEmailOptions
{
    To = "reader@example.com",
    TemplateKey = "monthly-news",
    Subject = "This month",
    Html = "<p>News</p><a href=\"{{ unsubscribe_url }}\">Unsubscribe</a>",
    Category = new MessageCategory
    {
        Key = "newsletter",
        Type = MessageCategoryType.Marketing,
        Name = "Newsletter",
        Description = "Monthly product news",
    },
});
```
