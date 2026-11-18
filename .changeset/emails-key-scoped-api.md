---
"PostHog": patch
---

Send transactional emails, attachment uploads, and email status reads through the key-scoped `/api/messaging/` routes instead of `/api/projects/@current/messaging/`. PostHog instances that predate these routes answer 404.
