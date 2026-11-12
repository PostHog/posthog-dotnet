---
"PostHog": minor
---

Adds `PostHogOptions.PromptRequestTimeout` (default 10 seconds). A prompt fetch that takes longer fails like a network error, so the caller gets the cached version, the fallback, or a `PromptFetchException` with the code `network_error`, instead of waiting for the HTTP client's own timeout.
