---
"PostHog": patch
"PostHog.AspNetCore": patch
"PostHog.AI": patch
---

Serialize non-finite float and double event properties as JSON null so they do not prevent an event batch from being sent.
