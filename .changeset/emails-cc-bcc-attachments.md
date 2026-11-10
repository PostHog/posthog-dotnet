---
"PostHog": minor
---

`SendCodeEmailOptions` takes `Cc`, `Bcc` and `Attachments`. An `EmailAttachment` is a `Filename` and its `Content` as bytes, with an optional `ContentType` and `ContentId` for inline images. An email carries at most 10 files and at most 40 MB after Base64 encoding, about 29 MB of files; the client checks both before any request and throws `too_many_attachments` or `message_too_large`. It names each file by its SHA-256, uploads the files PostHog doesn't have yet straight to PostHog's file storage without the secret key, through an `HttpClient` of their own (`PostHogClient.Uploads`) that never follows a redirect, and sends the email only after every upload succeeded. A refused upload throws `attachment_upload_failed` with the storage's answer in `Why`. Empty lists are left out of the request.
