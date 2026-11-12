---
"PostHog": minor
"PostHog.AspNetCore": minor
---

Add opt-in runtime exception capture with `PostHogOptions.CaptureUnhandledExceptions`. With `CaptureExceptions` enabled, the ASP.NET Core middleware now reports escaping request failures as unhandled and also captures exceptions consumed by a downstream error handler such as `UseExceptionHandler`. Nested entries in `$exception_list` now use mechanism type `chained` and omit `handled`.
