---
"PostHog.AI": patch
---

The OpenAI handler now maps `max_completion_tokens` (used by reasoning models on Chat Completions) to `$ai_max_tokens`. Previously the limit was missing and appeared in `$ai_model_parameters` as a string.
