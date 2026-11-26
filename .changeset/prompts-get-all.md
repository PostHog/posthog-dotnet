---
"PostHog": minor
---

Adds `GetAllAsync` on the prompts client. It fetches every prompt in one request, at the version a label points to or at the latest version, and caches each one, so later `GetAsync` calls are served from the cache. Use it once at startup instead of fetching prompts one by one.
