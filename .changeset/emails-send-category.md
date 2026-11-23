---
"PostHog": minor
---

`SendCodeEmailOptions.Category` sends an email from code in a message category that you define in code with a `MessageCategory`: its `Key`, `Type` (`MessageCategoryType.Marketing` or `Transactional`), `Name` and an optional `Description`. Requires a PostHog server with code-send category support (older servers refuse `category` with an HTTP 400 unknown-field error) and code-send categories enabled for the project (otherwise PostHog refuses an explicit category object with `categories_not_enabled`). PostHog creates the category by its key during send validation (before delivery or opt-out refusal) when the project has none with that key, or restores an archived one, and uses `Name` and `Description` only at creation: a send never renames a category or changes its description, so change them later in PostHog. Both creating a new category and restoring an archived one count against the project's active-category limit (`too_many_categories`). Leave `Category` out to send transactional email with no category, as before, or to follow the transactional category of the template that `TemplateKey` names (when code-send categories are enabled for the project, creating no category); passing an explicit `Category` must match that template's category or PostHog refuses the send with `category_mismatch`. Pass `MessageCategory.Transactional` directly (rather than constructing `new MessageCategory { Key = "transactional", ... }`) to send transactional email with no category, which PostHog refuses with `category_mismatch` for a template in a category. PostHog checks whether a template is enabled for transactional sends first (`template_not_transactional` otherwise), and refuses an otherwise sendable template in a marketing category with `marketing_template`. A marketing email from code honors the recipient's opt-outs (`recipient_opted_out`), needs a clickable `<a href="{{ unsubscribe_url }}">` link in its `Html` (or the `{{ unsubscribe_url }}` placeholder in its `Text` when it has no `Html`, else `unsubscribe_link_required`), takes no copies (`copies_not_allowed`), and cannot carry `Attachments` (`workflow_can_wait`, because marketing sending limits can hold delivery; uploads happen before PostHog refuses the send).

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
