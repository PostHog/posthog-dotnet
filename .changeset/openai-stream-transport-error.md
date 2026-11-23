---
"PostHog.AI": patch
---

The OpenAI handler now records `$ai_is_error` (with the exception message) when a streamed response fails mid-read, such as a dropped connection. Previously the generation was reported as a success with HTTP 200 while the caller got the exception.
