---
"PostHog": patch
---

Flush queued events when the process exits. A `PostHogClient` that is never disposed no longer loses the events still in its queue at a normal exit. The final flush waits at most 5 seconds. Disposing the client removes the handler, as before.
