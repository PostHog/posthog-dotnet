---
"PostHog": minor
---

`SendCodeEmailOptions` takes `Tracking` to choose what PostHog tracks for the email. Left unset, PostHog counts opens and captures engagement events, and doesn't track clicks. `EmailTracking.All` (or `true`) turns on opens, clicks and engagement events, and `EmailTracking.None` (or `false`) turns them all off. `new EmailTracking { Clicks = true }` sets `Opens`, `Clicks` and `Engagement` one by one. `CaptureEngagement` is now obsolete in favor of `Tracking` with `Engagement`; it still works, but a send can't set both. Left unset, the send body stays as it was. The field needs PostHog's API support, which is rolling out.
