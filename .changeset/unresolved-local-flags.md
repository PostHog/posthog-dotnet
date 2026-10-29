---
"PostHog": minor
---

Add `FeatureFlagEvaluations.UnresolvedFlags`, which lists the flags in an `EvaluateFlagsAsync` snapshot that have a loaded local definition but that neither local evaluation nor the remote fallback resolved, each with an `UnresolvedFlagReason`. Log a warning once for each flag whose definition local evaluation can never resolve.

Behavior changes:

- A condition with a filter that local evaluation cannot check and a filter that does not match now resolves locally as no match, instead of falling back to a server request or staying unresolved. A static cohort forces the server fallback only when it is the first filter in its condition that local evaluation cannot check.
- An inactive flag with experience continuity enabled now resolves to `false` locally instead of making a server request.
- `$feature_flag_called` events from snapshot reads report `local_evaluation_inconclusive` instead of `flag_missing` for unresolved flags.
- When definitions load, the SDK logs a warning that names each flag with experience continuity (`experience_continuity`) or with something local evaluation does not support, such as a static cohort, an unknown operator, or a missing or circular dependency (`unsupported_definition`). It logs the warning again only when the definition of that flag changes. A static cohort no longer logs an error on each evaluation.
