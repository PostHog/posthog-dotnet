---
"PostHog": patch
---

Fix `CaptureException` dropping exceptions whose stack trace passes through `MethodInfo.Invoke`, such as failed Hangfire jobs ([#349](https://github.com/PostHog/posthog-dotnet/issues/349)).
