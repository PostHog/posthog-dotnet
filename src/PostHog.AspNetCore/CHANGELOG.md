# PostHog.AspNetCore

## 2.6.3

### Patch Changes

- 16583c8: Fix `AsyncBatchHandler` background flushing so transient batch send failures no longer permanently stop future flushes. `FlushAsync()` now also waits for an in-progress flush instead of returning early without doing work.

## 2.6.2

### Patch Changes

- Updated dependencies [293539c]
  - PostHog@2.6.2

## 2.6.1

### Patch Changes

- Updated dependencies [188e99a]
  - PostHog@2.6.1

Previous release notes are available on the [GitHub releases page](https://github.com/PostHog/posthog-dotnet/releases).
