---
"PostHog": minor
---

Add `PromptResult.Compile`, which fills the `{{variable}}` placeholders of a fetched prompt, and `Prompts.ClearCache`, which removes cached prompts so the next `GetAsync` fetches them again. `ClearCache` takes no arguments to remove everything, a prompt name to remove every cached version and label of it, or a name and a version. `IPostHogPrompts` gains the `ClearCache` member, so a hand-written implementation of that interface needs to add it.
