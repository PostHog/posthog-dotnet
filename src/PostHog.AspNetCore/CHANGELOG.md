# PostHog.AspNetCore

## 2.7.0

### Minor Changes

- 9ea2412: Add feature flag evaluation contexts via `PostHogOptions.EvaluationContexts`. `/flags` requests now send `evaluation_contexts` when configured.
- 479f5ca: Add request-scoped server request context support for tracing headers and ASP.NET Core metadata.

### Patch Changes

- 2f5bded: Document public APIs and make `GroupCollection.TryAdd(Group)` store entries by group type instead of group key, matching the collection's one-group-per-type behavior.

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
