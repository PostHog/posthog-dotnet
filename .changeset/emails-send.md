---
"PostHog": minor
---

Add `PostHogClient.Emails`, also registered as `IPostHogEmails` by `AddPostHog`, to send transactional email and read its status. `SendAsync` sends a template made in PostHog (`SendTemplateEmailOptions`) or content your code rendered (`SendCodeEmailOptions`), with an idempotency key on every attempt and retries for transient failures. `GetAsync` reads an email's delivery status. Both need `SecretKey` set to a project secret API key. New options: `AppHost` (https only, unless `AllowInsecureAppHost` is set for local development), `AllowInsecureAppHost`, `EmailRequestTimeout`, `EmailRequestMaxRetries` and `MigratedFrom`.
