---
"PostHog": minor
"PostHog.AspNetCore": minor
---

Fall back to remote evaluation when a requested feature flag is missing from local definitions. This changes scoped calls from omitting the key without a request to making one `/flags` fallback per configured cache entry.
