import unittest

from check_coverage import EXPECTED_TESTS, check_health, check_report


def report_for(tests, status="✅"):
    lines = ["# posthog-dotnet Compliance Report"]
    for test in tests:
        suite, name = test.split(".", 1)
        lines.extend([
            f"## {suite.title()} Tests",
            f"| {name.replace('_', ' ').title()} | {status} | 1ms |",
        ])
    return "\n".join(lines) + "\n"


class CoverageChecksTests(unittest.TestCase):
    def test_health_advertises_capture_and_gzip(self):
        check_health({"sdk_name": "posthog-dotnet", "capabilities": ["capture_v0", "encoding_gzip"]})

    def test_health_without_capabilities_is_rejected(self):
        with self.assertRaises(ValueError):
            check_health({"sdk_name": "posthog-dotnet"})

    def test_current_inventory(self):
        self.assertEqual(len(EXPECTED_TESTS), 47)
        self.assertEqual(sum(test.startswith("capture.") for test in EXPECTED_TESTS), 30)
        check_report(report_for(EXPECTED_TESTS))

    def test_flags_only_report_is_rejected(self):
        with self.assertRaises(ValueError):
            check_report(report_for([test for test in EXPECTED_TESTS if test.startswith("feature_flags.")]))

    def test_empty_report_is_rejected(self):
        with self.assertRaises(ValueError):
            check_report(report_for([]))

    def test_missing_timestamp_case_is_rejected(self):
        with self.assertRaises(ValueError):
            check_report(report_for([test for test in EXPECTED_TESTS if "non_utc" not in test]))

    def test_duplicate_cannot_replace_missing_case(self):
        with self.assertRaises(ValueError):
            check_report(report_for(EXPECTED_TESTS[:-1] + [EXPECTED_TESTS[0]]))

    def test_assertion_failures_do_not_change_inventory(self):
        check_report(report_for(EXPECTED_TESTS, status="❌"))


if __name__ == "__main__":
    unittest.main()
