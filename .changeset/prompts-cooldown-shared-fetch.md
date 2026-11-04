---
"PostHog": patch
---

`Prompts.GetAsync` now starts the refetch cooldown inside the shared request, so a fetch that fails after every waiting caller cancelled still backs off. The failed fetch logs one warning, whether or not a caller was still waiting for it, and calls served from the cooldown do not log again.
