---
"PostHog": patch
---

Local evaluation of `is_date_before` and `is_date_after` now reads a date without an offset, such as `2024-01-15` or `2024-01-15 10:00:00`, and a `DateTime` of unspecified kind, as UTC instead of in the machine's time zone. A date that starts with `yyyy-MM-dd` is read with the invariant culture first, so a Thai, Persian or Hijri current culture no longer reads `2024-01-15` as a year centuries away or fails to parse it. Other formats are still read with the current culture.
