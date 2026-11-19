---
"PostHog.AI": patch
---

The OpenAI handler now recognises SSE `data:` lines that have no space after the colon, so streamed responses from such servers are tracked instead of being skipped.
