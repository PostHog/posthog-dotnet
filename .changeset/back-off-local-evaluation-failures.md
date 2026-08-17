---
"PostHog": patch
"PostHog.AspNetCore": patch
---

Back off after a failed local evaluation load and tolerate a malformed flag. A failed `/flags/definitions` load is now remembered for one poll interval, so a bad payload no longer refetches the definitions and falls back to the remote `/flags` endpoint on every flag call. A single flag with an unexpected shape is skipped instead of failing the whole definitions payload.
