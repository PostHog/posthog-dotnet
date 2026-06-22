---
"PostHog": minor
---

Support the `early_exit` filter option in local feature flag evaluation, mirroring the server-side evaluation engine. When a flag's `filters.early_exit` is `true` and a condition group's property filters match (or the group has none) but the rollout percentage excludes the user, evaluation stops and the flag returns a definitive disabled result instead of falling through to later condition groups. A pure property-filter mismatch always falls through, even when `early_exit` is enabled. When the field is absent or `false`, existing behavior is preserved.
