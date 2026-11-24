---
"PostHog": minor
---

Send the same transactional email to many recipients with `Emails.BatchAsync`, one email each, from a template with `TemplateData` per recipient (`SendTemplateBatchOptions`) or from content in your code (`SendCodeBatchOptions`). The client sends at most 100 recipients and 4 MiB per request, one request after another, waits up to 45 seconds for each answer, gives each recipient an idempotency key that every retry reuses, and returns a result per recipient in input order: `queued`, `failed`, `unconfirmed` or `not_attempted`. PostHog replays a key for 24 hours, so a batch sent again within that window queues no second email for a recipient it already accepted. A request that fails as a whole stops the batch and throws `EmailBatchException`, an `EmailException` whose `Results` hold every recipient. `EmailException` is no longer sealed.
