# PostHog

## 2.7.1

### Patch Changes

- 16583c8: Fix `AsyncBatchHandler` background flushing so transient batch send failures no longer permanently stop future flushes. `FlushAsync()` now also waits for an in-progress flush instead of returning early without doing work.

## 2.7.0

### Minor Changes

- db7fe08: Add the static `PostHogSdk` facade in the `PostHog.Sdk` namespace.

## 2.6.2

### Patch Changes

- 293539c: Disable the client without logging a project token error when the SDK is explicitly disabled or the project token is missing.

## 2.6.1

### Patch Changes

- 188e99a: test: release process

Previous release notes are available on the [GitHub releases page](https://github.com/PostHog/posthog-dotnet/releases).
