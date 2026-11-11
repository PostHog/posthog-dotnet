---
"PostHog": minor
"PostHog.AspNetCore": patch
---

Add opt-in runtime exception capture and report escaping ASP.NET Core request failures as unhandled. Nested entries in `$exception_list` now use mechanism type `chained` and omit `handled`.
