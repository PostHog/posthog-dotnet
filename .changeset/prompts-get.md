---
"PostHog": minor
---

Add `PostHogClient.Prompts`, also registered as `IPostHogPrompts` by `AddPostHog`, to fetch LLM prompts managed in PostHog. `GetAsync` fetches a prompt by name, at its latest version or a pinned `Version` or `Label`, with an in-memory cache (`PostHogOptions.PromptCacheTtl`, default 5 minutes, per-call override), the stale cached version when a refetch fails (with a cooldown before the next attempt that honors `Retry-After`), and an optional `Fallback`. Concurrent calls for the same prompt share one request. It needs `SecretKey` set to a personal API key with the `llm_prompt:read` scope.
