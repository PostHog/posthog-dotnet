# PostHog

## 2.8.5

### Patch Changes

- 256d2df: Add a per-client circuit breaker for feature flag requests after consecutive transient network failures, temporarily failing fast before probing for recovery.

## 2.8.4

### Patch Changes

- 211aa24: Add a feature flag request option for disabling GeoIP enrichment.

## 2.8.3

### Patch Changes

- 7bab8dc: Fall back to uncompressed batch uploads when local gzip compression fails.

## 2.8.2

### Patch Changes

- 0da29c6: Retry feature flag requests after transient network errors only. The feature flag request retry count defaults to 1 and can be set to 0 to disable retries.

## 2.8.1

### Patch Changes

- 60a2194: Preserve per-capture GeoIP override properties when super properties are configured.

## 2.8.0

### Minor Changes

- 788d9e0: Support the `early_exit` filter option in local feature flag evaluation, mirroring the server-side evaluation engine. When a flag's `filters.early_exit` is `true` and a condition group's property filters match (or the group has none) but the rollout percentage excludes the user, evaluation stops and the flag returns a definitive disabled result instead of falling through to later condition groups. A pure property-filter mismatch always falls through, even when `early_exit` is enabled. When the field is absent or `false`, existing behavior is preserved.
- 788d9e0: Add feature flag evaluation contexts via `PostHogOptions.EvaluationContexts`. `/flags` requests now send `evaluation_contexts` when configured.
- 788d9e0: Add a configurable `$is_server` event property (default `true`) so PostHog can identify server-side events. Set `PostHogOptions.IsServer` to `false` when using the SDK as a client/CLI so the device OS is attributed normally.
- 788d9e0: Add request-scoped server request context support for tracing headers and ASP.NET Core metadata.

### Patch Changes

- 788d9e0: Refactor duplicate internal SDK code paths without changing public API behavior.
- 788d9e0: Document public APIs and make `GroupCollection.TryAdd(Group)` store entries by group type instead of group key, matching the collection's one-group-per-type behavior.
- 788d9e0: Include group context in the `$feature_flag_called` dedupe cache key so group-scoped flags fire a separate event for each group a user is evaluated under, instead of being dedup-ed against the first group context the same `(distinctId, featureKey, response)` was seen under. The groups are canonicalized order-independently (`OrderBy(GroupType, StringComparer.Ordinal)`) so two equal collections built in different insertion orders still dedupe.
- 788d9e0: Return no-op results instead of throwing from public APIs when PostHog API calls fail.
- 788d9e0: Reject semver values with leading zeros in local flag evaluation. Per semver 2.0.0 §2, numeric identifiers must not include leading zeros — values like `1.07.3` are not valid semver and should not match targeting conditions. Both override values and flag values are now validated; invalid inputs cause `SemanticVersion.TryParse` to return false so the condition does not match.
- 788d9e0: Use the correct historical_migration wire field for batch capture payloads.

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
