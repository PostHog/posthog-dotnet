---
"PostHog.AspNetCore": patch
---

Picks up the `PostHog` fix for canceled flag definition loads. When the caller's cancellation token is canceled while flag definitions load for local evaluation, the PostHog feature manager now stops with `OperationCanceledException` instead of logging "Unable to get feature flags and payloads".
