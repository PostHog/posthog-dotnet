---
"PostHog": minor
---

`SendCodeEmailOptions.Category` sends an email from code as marketing email in one of the project's message categories, named by its key. Leave it out, or pass `EmailCategories.Transactional`, to send transactional email as before. A marketing email honors the recipient's opt-outs and needs a `{{ unsubscribe_url }}` placeholder in its content.
