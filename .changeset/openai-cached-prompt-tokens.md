---
"PostHog.AI": patch
---

Record OpenAI cached prompt tokens (`usage.prompt_tokens_details.cached_tokens`) in `$ai_cache_read_input_tokens` so cached requests are no longer costed at the full input price.
