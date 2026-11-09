---
"PostHog": minor
---

`SendCodeEmailOptions` takes `TrackOpensAndClicks`. Set it to `false` to send the email without open and click tracking: no open pixel and no links rewritten to count clicks, so PostHog can't count its opens and clicks. It is independent of `CaptureEngagement`, which decides whether engagement events are captured on the recipient's person. Left unset, the send body stays as it was and PostHog tracks the email. The field needs PostHog's API support, which is rolling out.
