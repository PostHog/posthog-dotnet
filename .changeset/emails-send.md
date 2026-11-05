---
"PostHog": minor
---

The new `PostHogClient.Emails` sends transactional email from your .NET code and reads each email's delivery status.

```csharp
using PostHog.Emails;

await using var posthog = new PostHogClient(new PostHogOptions
{
    ProjectToken = "phc_...",
    SecretKey = "phs_...",
});

var sent = await posthog.Emails.SendAsync(new SendCodeEmailOptions
{
    To = "user@example.com",
    DistinctId = "user_123",
    TemplateKey = "password-reset",
    Subject = "Reset your password",
    Html = renderedHtml,
});

var status = await posthog.Emails.GetAsync(sent.Id);
Console.WriteLine(status.Status); // "delivered", "bounced", ...
```

You can also send a template made in PostHog with `SendTemplateEmailOptions` and its `TemplateId`.

You need a project secret API key, the kind that starts with `phs_`. Create one under **Project secret API keys** in your project settings. Set its **Transactional email** scope to Write to send emails, or to Read for a key that only reads statuses. Set it as `SecretKey` on `PostHogOptions`, or as `PostHog:SecretKey` in your configuration when you use `AddPostHog`. Set `AppHost` to your PostHog app URL when you self-host PostHog or send through a reverse proxy.

PostHog records each email's delivery, opens and clicks on the person with that `DistinctId`. It groups every email with the same `TemplateKey`, so you see their metrics and logs in one place. The SDK retries a request that hits a network error, a rate limit or an outage twice by default. Every send carries an idempotency key, so a retry never sends the email twice. Pass your own `IdempotencyKey`, such as `welcome:user_123`, and your job runner's retries are safe too.

`SendCodeEmailOptions` also takes `From`, `ReplyTo` and `CaptureEngagement = false`. Set `MigratedFrom` on `PostHogOptions` to tell PostHog which email provider you moved from. A failed call throws an `EmailException` with PostHog's `Code` and the HTTP `StatusCode`.

Docs: [Send transactional emails with .NET](https://posthog.com/docs/workflows/transactional-emails/dotnet) and the [transactional email overview](https://posthog.com/docs/workflows/transactional-emails).
