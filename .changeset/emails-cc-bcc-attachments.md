---
"PostHog": minor
---

`SendCodeEmailOptions` takes `Cc`, `Bcc` and `Attachments`. An `EmailAttachment` is a `Filename` and its `Content` as bytes, which the client sends as Base64, with an optional `ContentType` and `ContentId` for inline images. Empty lists are left out of the request. A PostHog version that doesn't take one of these fields yet refuses the send with a `validation_error` that names the field, instead of sending without it.
