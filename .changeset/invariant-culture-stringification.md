---
"PostHog": patch
---

Use the invariant culture when stringifying property values for feature flag local evaluation. Numeric property values such as `3.14` now stringify as `"3.14"` regardless of the host locale, so `exact`, `icontains`, `starts_with`, `ends_with`, and regex matching behave the same way the PostHog flags service does on machines using comma-decimal cultures.
