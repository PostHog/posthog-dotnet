---
"PostHog": patch
---

Local feature flag evaluation now skips each condition aggregated by a group without a usable key, instead of resolving the whole group flag to `false`, as the flags service does. A condition with an explicit `null` aggregation in a group flag is a person condition and still decides the flag. A group flag with a holdout and no usable group key now falls back to the `/flags` endpoint, because only the server can tell if the holdout applies.
