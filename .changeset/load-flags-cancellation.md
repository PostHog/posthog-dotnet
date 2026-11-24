---
"PostHog": patch
---

`LoadFeatureFlagsAsync` and other calls that fetch flag definitions for local evaluation now throw `OperationCanceledException` when the caller's cancellation token is canceled, instead of logging "Unable to get feature flags and payloads" and returning as if the load had finished. A request that times out is still logged and does not throw.
