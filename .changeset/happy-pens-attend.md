---
"PostHog": patch
"PostHog.AspNetCore": patch
---

Fix `AsyncBatchHandler` background flushing so transient batch send failures no longer permanently stop future flushes. `FlushAsync()` now also waits for an in-progress flush instead of returning early without doing work.
