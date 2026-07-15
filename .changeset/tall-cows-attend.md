---
"PostHog": minor
---

Add a `$feature_flag_has_experiment` boolean property to `$feature_flag_called` events when the server reports whether the flag is linked to an experiment. The property is omitted when the server does not report it (older deployments and legacy response formats).
