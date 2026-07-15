---
"PostHog": minor
---

Add a `$feature_flag_has_experiment` boolean property to every `$feature_flag_called` event, sourced from the server-reported `has_experiment` field on flag responses. Defaults to `false` when the server does not report the field.
