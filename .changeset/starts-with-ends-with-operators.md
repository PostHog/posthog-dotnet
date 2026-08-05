---
"PostHog": minor
---

Support the `starts_with`, `not_starts_with`, `ends_with`, and `not_ends_with` property filter operators in feature flag local evaluation. Matching is case-insensitive and mirrors `icontains`, so flags using these operators no longer fail local evaluation.

Property filter operators this SDK version doesn't recognize now deserialize as `ComparisonOperator.Unknown` instead of failing the entire local evaluation response, so only the affected flag falls back to remote evaluation.
