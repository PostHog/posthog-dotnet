---
"PostHog": minor
---

Send minimal `$feature_flag_called` events when the server enables it (`minimalFlagCalledEvents` in the `/flags` v2 response or `minimal_flag_called_events` in the local evaluation payload) and the evaluated flag is not linked to an experiment. Minimal events keep a strict allowlist of flag evaluation properties and strip everything else, including the `$feature/<key>` enumeration and super properties. Experiment-linked flags and responses that do not carry the field continue to send the full event.
