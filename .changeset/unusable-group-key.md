---
"PostHog": patch
---

Local feature flag evaluation now treats an empty or null group key as no group key, as the flags service does. A condition aggregated on that group type is skipped, and a flag whose conditions are all skipped this way resolves to `false` locally without a `/flags` request. A null group key no longer throws when a `$feature_flag_called` event is captured.
