"""Check health discovery and the test inventory of harness 1.0.0 reports."""

import argparse
from collections import Counter
import json
from pathlib import Path
import re
from urllib.request import urlopen


EXPECTED_TESTS = Path(__file__).with_name("expected-tests.txt").read_text().splitlines()


def check_health(health):
    if health.get("sdk_name") != "posthog-dotnet":
        raise ValueError("Expected the posthog-dotnet adapter")
    if set(health.get("capabilities", [])) != {"capture_v0", "encoding_gzip"}:
        raise ValueError("Expected capture_v0 and encoding_gzip capabilities")


def check_report(report):
    # The reusable workflow emits Markdown, including every passed and failed test.
    # Check selection only: SDK assertion failures remain advisory in CI.
    if not report.startswith("# posthog-dotnet Compliance Report\n"):
        raise ValueError("Expected a posthog-dotnet compliance report")
    actual = []
    suite = None
    for line in report.splitlines():
        heading = re.fullmatch(r"## (\w+) Tests", line)
        if heading:
            suite = heading[1].lower()
        row = re.fullmatch(r"\| (.+) \| [✅❌] \| \d+ms \|", line)
        if row:
            actual.append(f"{suite}.{row[1].lower().replace(' ', '_')}")
    if not EXPECTED_TESTS or Counter(actual) != Counter(EXPECTED_TESTS):
        missing = sorted((Counter(EXPECTED_TESTS) - Counter(actual)).elements())
        unexpected = sorted((Counter(actual) - Counter(EXPECTED_TESTS)).elements())
        raise ValueError(f"Incorrect test inventory: missing={missing}, unexpected={unexpected}")
    print("Inventory verified: 30 capture + 17 feature_flags = 47 tests")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--health-url", help="Running adapter's /health URL")
    parser.add_argument("--report", type=Path, help="Harness Markdown report")
    args = parser.parse_args()
    if not args.health_url and not args.report:
        parser.error("provide --health-url or --report")
    if args.health_url:
        with urlopen(args.health_url, timeout=10) as response:
            check_health(json.load(response))
        print("Health capabilities verified")
    if args.report:
        check_report(args.report.read_text())


if __name__ == "__main__":
    main()
