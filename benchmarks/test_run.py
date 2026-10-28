import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

import run


class CoordinatorTests(unittest.TestCase):
    def setUp(self):
        self.item = {
            "spec": {"id": "control", "operations": 10, "passes": ["timing"]},
            "fixture_digest": "a" * 64,
        }
        self.documents = {
            role: {"execution": {"build_digest": role}, "benchmarks": []}
            for role in ("base", "head")
        }

    def execute(self, role, spec, measurement_pass):
        return {
            "status": "ok",
            "pass": measurement_pass,
            "operations": 10,
            "build_digest": role,
            "elapsed_time_per_op": 123,
            "elapsed_ns": 1230,
        }

    def test_alternates_pairs_and_persists(self):
        calls = []

        def execute(role, spec, measurement_pass):
            calls.append(role)
            return self.execute(role, spec, measurement_pass)

        with tempfile.TemporaryDirectory() as directory:
            status = run.run_pairs(
                self.documents,
                {r: r for r in self.documents},
                [self.item],
                3,
                Path(directory),
                execute,
            )
            self.assertEqual(status, 0)
            self.assertEqual(calls, ["base", "head", "head", "base", "base", "head"])
            for role in self.documents:
                document = json.loads((Path(directory) / f"{role}.json").read_text())
                self.assertEqual(
                    len(document["benchmarks"][0]["metrics"][0]["samples"]), 3
                )

    def test_timeout_budget_finishes_pair_and_stops(self):
        calls = []

        def execute(role, spec, measurement_pass):
            calls.append(role)
            return {
                "status": "timeout" if role == "head" else "ok",
                "pass": measurement_pass,
                "operations": 10,
                "build_digest": role,
                "elapsed_time_per_op": 123,
            }

        with tempfile.TemporaryDirectory() as directory:
            status = run.run_pairs(
                self.documents,
                {r: r for r in self.documents},
                [self.item] * 2,
                6,
                Path(directory),
                execute,
            )
            self.assertEqual(status, 1)
            self.assertEqual(calls, ["base", "head", "head", "base"])
            self.assertEqual(len(self.documents["base"]["benchmarks"]), 1)
            self.assertEqual(len(self.documents["base"]["benchmarks"][0]["runs"]), 2)
            self.assertEqual(
                self.documents["head"]["benchmarks"][0]["status"], "timeout"
            )

    def test_wrong_build_is_failure(self):
        def execute(role, spec, measurement_pass):
            return {
                **self.execute(role, spec, measurement_pass),
                "build_digest": "wrong",
            }

        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(
                run.run_pairs(
                    self.documents,
                    {r: r for r in self.documents},
                    [self.item],
                    1,
                    Path(directory),
                    execute,
                ),
                1,
            )
            self.assertEqual(self.documents["head"]["benchmarks"][0]["metrics"], [])

    def test_invalid_measurement_does_not_leave_successful_run(self):
        def execute(role, spec, measurement_pass):
            return {
                **self.execute(role, spec, measurement_pass),
                "allocated_bytes_per_op": float("nan"),
            }

        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(
                run.run_pairs(
                    self.documents,
                    {r: r for r in self.documents},
                    [self.item],
                    1,
                    Path(directory),
                    execute,
                ),
                1,
            )
            benchmark = self.documents["head"]["benchmarks"][0]
            self.assertEqual(benchmark["metrics"], [])
            self.assertEqual([r["status"] for r in benchmark["runs"]], ["error"])

    def test_missing_rss_is_unsupported_not_zero(self):
        benchmark = {"status": "ok", "metrics": [], "runs": []}
        run.append_sample(
            benchmark, {"status": "ok", "pass": "memory", "operations": 10}, 0
        )
        self.assertEqual(benchmark["metrics"][0]["status"], "unsupported")
        self.assertNotIn("samples", benchmark["metrics"][0])

    def test_environment_removes_external_runtime_tuning(self):
        with patch.dict(
            os.environ,
            {
                "COMPlus_TieredCompilation": "1",
                "DOTNET_GCHeapHardLimit": "1",
                "CORECLR_ENABLE_PROFILING": "1",
            },
        ):
            env = run.environment()
        self.assertNotIn("COMPlus_TieredCompilation", env)
        self.assertNotIn("CORECLR_ENABLE_PROFILING", env)
        self.assertEqual(env["DOTNET_GCHeapHardLimit"], "20000000")

    def test_timeout_kills_and_reaps_worker(self):
        process = MagicMock()
        process.__enter__.return_value = process
        process.wait.side_effect = [subprocess.TimeoutExpired("dotnet", 0.01), None]
        process.returncode = -9
        with patch.object(run.subprocess, "Popen", return_value=process):
            raw = run.run_worker(
                Path("unused"), self.item["spec"], "timing", timeout=0.01
            )
        process.kill.assert_called_once()
        self.assertEqual(process.wait.call_count, 2)
        self.assertEqual(raw["status"], "timeout")

    def test_dependency_graph_is_sdk_metadata(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            sdk = root / "src/PostHog"
            sdk.mkdir(parents=True)
            (sdk / "packages.lock.json").write_text("{}")
            worker = root / "Benchmark.dll"
            worker.with_suffix(".deps.json").write_text('{"PostHog": "2.17.1"}')
            with (
                patch(
                    "sys.argv",
                    [
                        "run.py",
                        "--base",
                        directory,
                        "--head",
                        directory,
                        "--output",
                        str(root / "results"),
                        "--build-directory",
                        str(root / "build"),
                    ],
                ),
                patch.object(run, "prepare", return_value=worker),
                patch.object(run, "describe", side_effect=lambda *_: {"workloads": []}),
                patch.object(run, "git", return_value="a" * 40),
                patch.object(run, "cpu_model", return_value="control CPU"),
                patch.object(run.subprocess, "check_output", return_value="9.0.109"),
                patch.object(run.subprocess, "run"),
                patch.object(run, "run_pairs", return_value=0) as pairs,
            ):
                self.assertEqual(run.main(), 0)
            execution = pairs.call_args.args[0]["head"]["execution"]
            self.assertEqual(
                execution["packages"]["application_dependency_graph_sha256"],
                run.digest(worker.with_suffix(".deps.json").read_bytes()),
            )
            # The reporter excludes packages from environment compatibility, but
            # treats unknown top-level execution fields as environment identity.
            self.assertNotIn("application_dependency_digest", execution)

    def test_signal_exit_is_not_proof_of_oom(self):
        process = MagicMock()
        process.__enter__.return_value = process
        process.returncode = -9
        with patch.object(run.subprocess, "Popen", return_value=process):
            raw = run.run_worker(Path("unused"), self.item["spec"], "timing")
        self.assertEqual(raw["status"], "error")
        process.kill.assert_not_called()


if __name__ == "__main__":
    unittest.main()
