---
"PostHog": patch
---

Treat a malformed, empty, or whitespace-only feature flag payload as no payload instead of throwing during flag construction, so the flag's evaluated value and its healthy sibling flags and payloads survive. The failure is logged as a warning.
