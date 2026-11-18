---
"PostHog": patch
---

Local evaluation of `is_date_before` and `is_date_after` now compares `DateTime` and `DateOnly` property values. A `DateTime` against an absolute date, and a `DateOnly` against any date, used to match `is_date_after` and miss `is_date_before` whatever the value. A `DateTime` near `DateTime.MinValue` no longer throws `ArgumentOutOfRangeException` east of UTC, and an unsupported relative date unit such as `-1D` is now inconclusive.
