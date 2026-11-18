---
"PostHog": patch
---

Send transactional emails, attachment uploads, and email status reads through the key-scoped `/api/messaging/` routes instead of `/api/projects/@current/messaging/`. Needs PostHog Cloud, or a self-hosted PostHog from 2026-11-18 or later. Older self-hosted versions answer these calls with a 404.
