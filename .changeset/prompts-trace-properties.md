---
"PostHog": minor
---

Adds `PromptResult.ToTraceProperties()`. It returns the `$ai_prompt_name` and `$ai_prompt_version` event properties for the prompt, so a captured `$ai_generation` event links to the prompt version that produced it. The dictionary is empty for a code fallback.
