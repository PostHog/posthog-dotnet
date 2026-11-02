---
"PostHog": minor
---

Add `IPostHogClient.Emails` to send transactional email and read its status. `SendAsync` sends a template made in PostHog (`SendTemplateEmailOptions`) or content your code rendered (`SendCodeEmailOptions`), with an idempotency key on every attempt and retries for transient failures. `GetAsync` reads an email's delivery status. Both need `SecretKey` set to a project secret API key. New options: `AppHost`, `EmailRequestTimeout`, `EmailRequestMaxRetries` and `MigratedFrom`.
