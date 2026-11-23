---
"PostHog": minor
---

Schedule a transactional email up to 7 days ahead on PostHog Cloud with `ScheduledAt` on `SendTemplateEmailOptions` and `SendCodeEmailOptions`. The client rounds the time up to the next millisecond, sends it in UTC, and sends the same time on every retry. A client whose app host isn't PostHog Cloud US or EU fails with `scheduling_unsupported` before any request, once its other app host checks pass. PostHog queues a scheduled code send with its rendered content until the email goes out. When PostHog accepts the email without confirming the time, the send fails with `scheduling_unconfirmed`, and the new `EmailException.MessageId` names the email. `SendEmailResult` now carries `Status` (`scheduled` or `queued`) and `ScheduledAt`, and `GetAsync` reads `scheduled` while the email waits.
