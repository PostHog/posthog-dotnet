---
"PostHog": minor
---

Move the static `PostHogSdk` facade to the `PostHog.Sdk` namespace and route its no-default-client warning through `Microsoft.Extensions.Logging`.
