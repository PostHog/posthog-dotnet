---
'PostHog': minor
---

Add `PostHogOptions.SecretKey` for local feature flag evaluation and remote config. It accepts either a Personal API Key (`phx_...`) or a Project Secret API Key (`phs_...`). The existing `PersonalApiKey` option is now a deprecated alias; when both are set, `SecretKey` takes precedence.
