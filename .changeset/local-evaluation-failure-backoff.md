---
"PostHog": patch
"PostHog.AspNetCore": patch
---

Stop the local evaluation loader from refetching flag definitions on every flag evaluation after a load fails. A persistent load failure now backs off for one poll interval, so a bad payload or a quota limit no longer drives the endpoint into rate limiting (429).
