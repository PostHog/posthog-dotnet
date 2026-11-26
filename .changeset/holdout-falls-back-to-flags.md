---
"PostHog": patch
---

Local feature flag evaluation now treats any flag with a holdout above 0% as unresolved (`unsupported_definition`), because the SDK does not evaluate holdouts. These flags fall back to the `/flags` endpoint, or stay unresolved when `OnlyEvaluateLocally` is true, and flags that depend on them do the same. They are also named in the load-time warning for definitions local evaluation does not support. A group flag with a holdout and no usable group key still reports `missing_context`.
