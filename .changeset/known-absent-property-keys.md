---
"PostHog": minor
---

Add `PersonPropertyKeysKnownAbsent` and `GroupPropertyKeysKnownAbsent` to `AllFeatureFlagsOptions` and `FeatureFlagOptions`. Local evaluation treats a listed key as absent, so `is_not_set` and the negated operators match without a `/flags` request. When the SDK falls back to `/flags`, it sends the listed keys. Each list holds at most 1000 keys; a longer list throws an `ArgumentException`. Calls that list any key skip the feature flag cache.
