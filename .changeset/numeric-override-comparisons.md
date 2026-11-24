---
"PostHog": patch
---

Local evaluation of `gt`, `gte`, `lt` and `lte` now compares property values of type `float`, `decimal`, `short`, `ushort`, `uint`, `ulong`, `byte` and `sbyte` as numbers. They used to be compared as strings, so `10` was less than `7`.
