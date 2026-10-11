---
"PostHog": minor
---

Add `FeatureFlagEvaluations.IsEnabled(key, defaultValue)`, which returns the caller-supplied default when the flag has no value in the snapshot. A flag that has a value — including `false` — still wins over the default.
