---
"PostHog": patch
---

Local evaluation now leaves a flag unresolved with the `unsupported_definition` reason when one of its condition filters has a type the SDK can't evaluate, such as `person_metadata`. Before, such a filter failed loading of every flag definition. A property filter whose `type` is omitted, `null`, or empty now matches as a person filter, in flag conditions and in cohorts.
