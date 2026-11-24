---
"PostHog.AI": patch
---

The OpenAI handler now captures the Responses API `instructions` as a leading system message in `$ai_input`, and maps `max_output_tokens` to `$ai_max_tokens`. Previously the system prompt was dropped and both fields appeared in `$ai_model_parameters` as strings.
