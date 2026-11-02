---
"PostHog": patch
"PostHog.AspNetCore": patch
---

Allow local feature flag definitions to load when cohorts contain flag dependencies or unknown filter types. Affected flags fall back to remote evaluation, or are reported as unresolved when evaluating only locally, without blocking unrelated flags.
