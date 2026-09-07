# Contributing

This package contains the PostHog .NET SDK compliance adapter used with the PostHog SDK Test Harness.

## Running tests

Tests run automatically in CI via GitHub Actions using harness 1.0.0. The adapter
references the core SDK project and calls `PostHogClient.Capture`, `FlushAsync`,
and `GetFeatureFlagAsync`; the SDK owns serialization, compression, retries,
flag results, and feature-flag-called events. The adapter runs on .NET 9 and uses
the SDK's .NET 8 target.

Health advertises `capture_v0` and `encoding_gzip`. Normal server-wire discovery
selects 30 V0 capture tests (including UTC timestamp overrides and gzip) and 17
feature flag tests. `expected-tests.txt` records the full 47-test inventory.
V1, dedicated AI capture, and non-gzip codecs are not supported by this profile.
Feature flags exercise the existing single-key public getter with remote calls
and the adapter's configured options, not every SDK overload or default.

CI keeps SDK assertion failures advisory, but verifies the report contains the
complete inventory. Missing reports, empty runs, and capture discovery regressions
fail the inventory check.

### Focused coverage checks

These checks use Python 3's standard library:

```bash
python3 -m unittest discover -s sdk_compliance_adapter -p 'test_*.py'
python3 sdk_compliance_adapter/check_coverage.py --health-url http://localhost:8080/health
python3 sdk_compliance_adapter/check_coverage.py --report sdk-compliance-report.md
```

The health check requires a running adapter. The report check accepts the Markdown
report emitted by the pinned reusable workflow and checks selection independently
of assertion outcomes.

### Locally with Docker Compose

Run the full compliance suite from the `sdk_compliance_adapter` directory:

```bash
docker-compose up --build --abort-on-container-exit
```

This will:

1. Build the .NET SDK adapter
2. Pull the test harness image
3. Run all compliance tests
4. Show the results

### Manually with Docker

```bash
# Create network
docker network create test-network

# Build and run adapter
docker build -f sdk_compliance_adapter/Dockerfile -t posthog-dotnet-adapter .
docker run -d --name sdk-adapter --network test-network -p 8080:8080 posthog-dotnet-adapter

# Run test harness
docker run --rm \
  --name test-harness \
  --network test-network \
  ghcr.io/posthog/sdk-test-harness:1.0.0 \
  run --adapter-url http://sdk-adapter:8080 --mock-url http://test-harness:8081

# Cleanup
docker stop sdk-adapter && docker rm sdk-adapter
docker network rm test-network
```
