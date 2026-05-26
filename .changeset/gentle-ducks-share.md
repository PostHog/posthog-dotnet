---
"PostHog": patch
"PostHog.AspNetCore": patch
"PostHog.AI": patch
---

Document public APIs and make `GroupCollection.TryAdd(Group)` store entries by group type instead of group key, matching the collection's one-group-per-type behavior.
