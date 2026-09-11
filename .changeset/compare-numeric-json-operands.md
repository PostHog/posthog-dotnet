---
"PostHog": patch
"PostHog.AspNetCore": patch
---

Fix `gt`, `gte`, `lt` and `lte` matching everyone when the operand arrives as a JSON number rather than a string. A bare number in a filter was assumed to be a cohort reference, so it was read through the cohort-id constructor and left nothing for the comparison to compare against, which made every one of these operators pass whatever the person's property was. The comparison now falls back to that value. A fractional operand such as `21.5` also reached `GetInt64`, which throws and failed the whole local evaluation payload rather than one flag; it is now kept as its invariant text. Both sides of a numeric comparison parse with the invariant culture, so a locale that groups thousands with `.` no longer reads a person's `2.5` as `25`.
