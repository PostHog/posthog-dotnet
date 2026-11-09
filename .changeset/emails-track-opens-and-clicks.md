---
"PostHog": minor
---

`SendCodeEmailOptions` takes `TrackOpensAndClicks`. Set it to `false` to send the email without PostHog's open pixel, so PostHog can't count its opens. PostHog doesn't track clicks on emails sent from code, so their links are never rewritten either way. It is independent of `CaptureEngagement`, which decides whether engagement events are captured on the recipient's person. Left unset, the send body stays as it was and PostHog tracks the email's opens. The field needs PostHog's API support, which is rolling out.
