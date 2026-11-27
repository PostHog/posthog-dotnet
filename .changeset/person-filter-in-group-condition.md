---
"PostHog": patch
---

Local evaluation now reads the person properties and the person known-absent keys for a person filter, or a person cohort leaf, in a condition aggregated on a group, matching `/flags`. Before, it read the group's properties.
