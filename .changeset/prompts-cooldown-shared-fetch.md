---
"PostHog": patch
---

`Prompts.GetAsync` now starts the refetch cooldown inside the shared request, so a fetch that fails after every waiting caller cancelled still backs off. Calls served from the cooldown no longer repeat the stale-cache and fallback warnings; the failed fetch logs them once.
