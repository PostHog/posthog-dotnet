---
"PostHog": patch
---

Honor definition snapshot `property_matching_version` during local feature flag evaluation. Version 2 uses normalized scalar and list-member equality instead of aggregate boolean coercion, including known null properties; missing and other versions retain legacy matching. Empty filter lists keep recursive truthiness in both modes. Matching semantics remain tied to cached definitions across refreshes and 304 responses.
