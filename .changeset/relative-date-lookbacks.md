---
"PostHog": patch
---

Local evaluation of `is_date_before` and `is_date_after` now reads a relative date without a minus sign, such as `7d`, as a lookback like `-7d`, instead of leaving the flag inconclusive. Month and year lookbacks step back one unit at a time and clamp the day after every step, so `-2m` from March 31 is January 28, not January 31, and `-4y` from February 29 is February 28. A lookback that falls before the year 1, such as `-3000y`, is now inconclusive instead of throwing `ArgumentOutOfRangeException`.
