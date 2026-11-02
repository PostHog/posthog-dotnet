---
"PostHog": patch
"PostHog.AspNetCore": patch
---

Allow local feature flag definitions to load when cohorts contain flag dependencies or unknown filter types. Affected flags fall back to remote evaluation, or are reported as unresolved when evaluating only locally, without blocking unrelated flags.

Correct nested cohort AND/OR evaluation to use the parent group's operator. Flags targeting OR-of-AND cohorts can now evaluate to true when any child group matches, instead of incorrectly returning false.
