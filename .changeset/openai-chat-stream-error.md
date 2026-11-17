---
"PostHog.AI": patch
---

OpenAI Chat Completions streams that end in an `{"error": ...}` chunk now set `$ai_is_error` and `$ai_error`.
