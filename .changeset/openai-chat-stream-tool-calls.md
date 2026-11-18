---
"PostHog.AI": patch
---

OpenAI Chat Completions streams now capture `delta.tool_calls` in `$ai_output_choices`, so generations that only call tools are no longer recorded with empty output.
