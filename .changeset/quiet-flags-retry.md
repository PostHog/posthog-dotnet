---
'PostHog': patch
'PostHog.AspNetCore': patch
---

Add a per-client circuit breaker for feature flag requests after consecutive transient network failures, temporarily failing fast before probing for recovery.
