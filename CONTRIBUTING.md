# Contributing

Thanks for your interest in improving the PostHog .NET SDK.

## Commit signing

This repo requires all commits to be signed. To configure commit signing, see the [PostHog handbook](https://posthog.com/handbook/engineering/security#commit-signing). Set `commit.gpgsign` to `true` in your git config so that every commit is signed, not only the ones you sign by hand.

## Building

From the repository root, use the same restore, formatting, build, and test flow that CI runs:

```bash
dotnet restore --locked-mode
bin/fmt --check
dotnet build --configuration Release --no-restore --nologo
dotnet test --configuration Release --no-build --nologo
```

## Running samples

Sample projects live in the `samples` directory.

To run the samples, set your PostHog project token from the repository root:

```bash
bin/user-secrets set PostHog:ProjectToken YOUR_PROJECT_TOKEN
```

The main ASP.NET Core sample app can then be started with:

```bash
bin/start
```

You can also run the samples from your preferred IDE or editor.

## Testing

The test projects target both `net8.0` and `netcoreapp3.1`. While .NET Core 3.1 reached end-of-life in December 2022, we continue to test against it because:

- It was the first runtime to fully support .NET Standard 2.1
- It serves as our minimum test baseline to ensure the `netstandard2.1` library works correctly on older runtimes
- It helps catch compatibility issues that might not surface on newer runtimes

This testing approach ensures broad compatibility without requiring users to install legacy runtimes in production.

## Performance benchmarks

Every opened, reopened, or updated pull request runs `.github/workflows/benchmarks.yml`.
It compares the merge base with the exact PR head, using the head's benchmark code for
both revisions. Results are informational: slow measurements do not fail the job, but
build, execution, fixture correctness, or delivery failures do. The separate trusted
`workflow_run` publisher posts a sticky PR comment once its workflow is on the default
branch. Reporting is provided by [PostHog/.github](https://github.com/PostHog/.github/blob/main/.github/workflows/sdk-benchmark-report.md).

The 20 workloads follow the shared [SDK benchmark specification](https://github.com/PostHog/.github/blob/main/.github/workflows/sdk-benchmark-specification.md)
(`suite.version=2`, `suite.specification=sdk-benchmarks/v1`). They cover nine single-flag
cases (16,000 evaluations each), all-flags at 10/100/1,000 definitions (1,600/160/160
evaluations), 1,600 load evaluations using at most eight ThreadPool tasks, 40 definition
reloads, and six capture workloads with 1,000 events each. Capture uses 32-byte and
4-KiB payloads for enqueue, steady delivery (explicit flush every 100), and burst
delivery (one final explicit flush). Enqueue has timing/allocation passes; both delivery
patterns have timing/allocation/memory passes. Single/all-flags have timing/allocation
passes; load and refresh have memory passes. Flag cases include percentage rollouts,
variants, properties, regexes, cohorts, early/late/no-match groups and inactive flags.
The suite exercises supported legacy getters; it does not measure the newer
`EvaluateFlagsAsync` snapshot API.

### Run locally

Install .NET SDK 9.0.109 (including runtime 9.0.8) and Python 3.12. The coordinator uses
only Python's standard library; all instrumentation and SDK workloads execute in C#.
Use a fresh build directory for each invocation:

```bash
python3 -B -m unittest discover -s benchmarks -p 'test_*.py'
RUN=$(mktemp -d)
python3 -B benchmarks/run.py \
  --base /path/to/baseline-checkout \
  --head "$PWD" \
  --build-directory "$RUN/build" \
  --output "$RUN/results" \
  --repetitions 6
```

Use `--smoke --repetitions 1` for correctness checks across all 20 workloads: 160
single-flag evaluations, 16 all-flags evaluations, 32 load evaluations, four refreshes,
and 200 events per capture workload. Smoke is not repeatability evidence. Native
controls run automatically before pairs and check the inventory, fixtures, phase reset,
flush schedules, reload generations, deterministic checkpoints and failure detection.
For a same-revision noise control, point both `--base` and `--head` at the same checkout.
Run repeatability checks on an otherwise idle machine; concurrent validation is only
correctness evidence. Do not compare results from different hosts or languages.

The coordinator copies the same standalone benchmark project outside the checkout,
builds it separately against each revision's public SDK project with locked NuGet
restore, and fingerprints the loaded SDK assembly and dependency graph. It uses the
SDK's `net8.0` target on pinned .NET 9.0.8. Benchmark workers use workstation GC and
optimized, non-tiered JIT compilation to avoid tier transitions during measurements.
This is one explicit runtime configuration, not a claim about all .NET deployments.
Each workload/repetition/pass starts a fresh process; base/head order alternates.
Warmup is 1,024 single evaluations, 16 all/load evaluations, two refreshes, or 100
captures, followed by a 25-ms native settling delay and full GC. Warmup captures are
flushed and verified before counters and bounded sequence state reset. Every measured
flag workload starts with generation 0 reloaded and verified. Full flag correctness
sweeps occur outside measurement, including after the memory post-settling snapshot;
measured refreshes only reload definitions and check that each made a request.

### Measurement boundaries

- Elapsed time uses `Stopwatch`; CPU uses whole-process `TotalProcessorTime`. Values
  are expressed in nanoseconds, but CPU-counter resolution is OS-dependent.
- Allocation volume uses `GC.GetTotalAllocatedBytes(precise: true)`, including managed
  objects collected during the operation. It excludes native allocations and does not
  provide exact object counts; counts are reported as unsupported, never zero.
- Idle and post-settling managed heap use `GC.GetTotalMemory(false)` after explicit
  full collections. These are retained-heap estimates, not allocation volume.
- RSS checkpoints follow every 32 completed load evaluations, each refresh, or every
  100 captures, including the final operation if needed. Steady checkpoints follow the
  scheduled flush; burst checkpoints precede the final flush. `observed_workload_peak_rss`
  is the maximum of before-work, checkpoints and after-work readings, not a continuous
  maximum. `process_lifetime_peak_rss` (`PeakWorkingSet64`) includes startup and warmup.
  `idle_process_rss`, `post_settling_process_rss` and `post_settling_heap_used` retain their
  distinct scopes. No forced GC occurs within measured work or checkpoint callbacks;
  full collections bracket memory measurement. GC heap bytes and RSS are not
  interchangeable; releasing objects need not reduce RSS.
- Public `IHttpClientFactory` injection supplies offline definitions and acknowledges
  batches. Measurements include in-process transport JSON parsing and validation, with
  compression disabled through public options. This is not a network or remote
  ingestion benchmark. No project credentials are required.
- Caller fixtures are constructed outside timing/allocation boundaries. Event `benchmark`
  uses 16 rotating identities and separate per-event property dictionaries containing
  ASCII `payload`, `plan=pro` and integer `sequence`, starting at zero for each phase.
  The receiver checks exact payload, plan and sequence-to-identity mapping using bounded,
  preallocated uniqueness state: balanced duplicate/loss fails; order is not required.
- Queue capacity (100,001), automatic flush count (100,000), and maximum count-based
  batch size (100,000) exceed the workload; polling and flush intervals are one hour.
  Native background consumers
  and size-based transport behavior remain active. The patterns specify caller flushes,
  not HTTP request counts. Enqueue measures synchronous `Capture` API return
  (`completion_boundary=api_return`), excluding its later flush and verification;
  process-wide CPU/allocation counters may include concurrent background work. Delivery
  includes capture preparation, serialization and public `FlushAsync` completion
  (`completion_boundary=completed_delivery`). Verification never adds a rescuing flush.

Each worker has a 120-second deadline and a 512-MiB **managed heap** limit, not an RSS
limit. After two worker timeouts the coordinator finishes the current pair, saves
partial diagnostics and fails the run. A killed process is not automatically classified
as OOM. Raw `base.json` and `head.json` retain statuses, metadata, repetitions and memory
checkpoints; the PR report suppresses numeric comparisons when the source run failed.

## Public API changes

Public API is hard to change once it ships, so agree on it before writing the implementation. Our [SDK guidelines](https://posthog.com/handbook/engineering/sdks/guidelines) explain how we design it.

This section is for external contributors. PostHog maintainers (members of the PostHog GitHub org) agree on API shape in the PR itself, so they don't need a separate issue.

- **Before you start:** if you need something the SDK doesn't support and it would add or change a public option, method, or type, open an issue describing your use case. Wait for a maintainer to agree on the API shape there before you implement it. Context is more useful to us than code at this stage.
- **Already specified?** If a published [sdk-spec](https://github.com/PostHog/sdk-specs) defines the API, that's the agreement, so you don't need an issue.
- **Already have a PR open?** Don't stop or rewrite it. Call out the public API change at the top of the PR description, and link or open an issue so we can discuss the shape there.
- Check first whether an existing option or hook, such as `BeforeSend`, already covers the use case. We avoid offering two ways to do the same thing.
- If a reviewer suggests a different API on your PR, confirm it with them before re-implementing. Treat it as a question, not an instruction.

PublicApiAnalyzers tracks each project's public API in `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`, and the build fails when they're out of date. A diff in those files means your change touches public API.

## Pull requests

Please follow existing conventions and include tests for your change when practical.

For release instructions, see [RELEASING.md](RELEASING.md).
