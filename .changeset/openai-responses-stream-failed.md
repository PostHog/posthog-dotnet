---
"PostHog.AI": patch
---

OpenAI Responses streams that end in `response.failed` or an `error` event now set `$ai_is_error` and `$ai_error`.
