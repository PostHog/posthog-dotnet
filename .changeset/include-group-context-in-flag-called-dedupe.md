---
"PostHog": patch
---

Include group context in the `$feature_flag_called` dedupe cache key so group-scoped flags fire a separate event for each group a user is evaluated under, instead of being dedup-ed against the first group context the same `(distinctId, featureKey, response)` was seen under. The groups are canonicalized order-independently (`OrderBy(GroupType, StringComparer.Ordinal)`) so two equal collections built in different insertion orders still dedupe.
