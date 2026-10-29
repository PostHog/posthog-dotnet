---
"PostHog.AI": patch
---

Decode OpenAI streaming responses with a stateful UTF-8 decoder so non-ASCII characters (accents, emoji) that are split across two reads are no longer replaced with U+FFFD in `$ai_output_choices`.
