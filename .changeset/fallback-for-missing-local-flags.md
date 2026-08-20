---
"PostHog": minor
"PostHog.AspNetCore": minor
---

Fall back to remote evaluation when a requested feature flag is missing from local definitions. This changes scoped calls from omitting the key without a request to making one direct `/flags` fallback per `EvaluateFlagsAsync` call.
