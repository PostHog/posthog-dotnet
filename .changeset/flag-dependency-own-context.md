---
"PostHog": patch
---

Local evaluation now evaluates a flag dependency like a standalone flag, with its own aggregation, properties and bucketing identifier. A person flag that a group flag depends on now reads the person properties and buckets by the distinct id, matching `/flags`.
