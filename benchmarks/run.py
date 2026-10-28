"""Build the head-side CLR worker against each SDK, then run isolated paired passes."""

import argparse
import copy
import hashlib
import json
import math
import os
import platform
import shutil
import subprocess
import tempfile
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent
TIMEOUT = 120
MAX_TIMEOUTS = 2


def digest(value):
    return hashlib.sha256(value).hexdigest()


def git(directory, *args):
    return subprocess.check_output(
        ["git", "-C", str(directory), *args], text=True
    ).strip()


def environment():
    # Keep machine-specific CLR tuning and diagnostics out of the paired workers.
    env = {
        k: v
        for k, v in os.environ.items()
        if not k.startswith(("DOTNET_", "COMPlus_", "CORECLR_", "COR_"))
    }
    env.update(
        DOTNET_TieredCompilation="0",
        DOTNET_gcServer="0",
        DOTNET_GCHeapHardLimit="20000000",
        DOTNET_CLI_TELEMETRY_OPTOUT="1",
    )
    return env


def cpu_model():
    if platform.system() == "Linux":
        for line in Path("/proc/cpuinfo").read_text().splitlines():
            if line.startswith("model name"):
                return line.split(":", 1)[1].strip()
    if platform.system() == "Darwin":
        return subprocess.check_output(
            ["sysctl", "-n", "machdep.cpu.brand_string"], text=True
        ).strip()
    return platform.processor() or platform.machine()


def prepare(source, directory):
    project = directory / "project"
    project.mkdir(parents=True, exist_ok=False)
    for path in sorted(HERE.iterdir()):
        if path.suffix in (".cs", ".csproj", ".json"):
            shutil.copy2(path, project / path.name)
    output = directory / "out"
    subprocess.run(
        [
            "dotnet",
            "publish",
            str(project / "Benchmark.csproj"),
            "--configuration",
            "Release",
            f"-p:PostHogProject={source / 'src/PostHog/PostHog.csproj'}",
            "-p:GeneratePackageOnBuild=false",
            "-p:RestoreLockedMode=true",
            "--output",
            str(output),
        ],
        check=True,
        env=environment(),
        cwd=project,
    )
    return output / "Benchmark.dll"


def describe(worker, smoke):
    return json.loads(
        subprocess.check_output(
            ["dotnet", str(worker), "describe", *(["--smoke"] if smoke else [])],
            env=environment(),
            text=True,
            timeout=TIMEOUT,
        )
    )


def run_worker(worker, spec, measurement_pass, timeout=TIMEOUT):
    with tempfile.TemporaryDirectory(prefix="posthog-dotnet-worker-") as temporary:
        root = Path(temporary)
        (root / "spec.json").write_text(json.dumps(spec))
        output = root / "result.json"
        # Files keep diagnostic output from growing the coordinator's memory.
        with (root / "stdout").open("w+b") as stdout, (root / "stderr").open(
            "w+b"
        ) as stderr:
            with subprocess.Popen(
                [
                    "dotnet",
                    str(worker),
                    "worker",
                    str(root / "spec.json"),
                    measurement_pass,
                    str(output),
                ],
                cwd=root,
                env=environment(),
                stdout=stdout,
                stderr=stderr,
            ) as process:
                timed_out = False
                try:
                    process.wait(timeout=timeout)
                except subprocess.TimeoutExpired:
                    timed_out = True
                    process.kill()
                    process.wait()
                logs = {}
                for name, stream in (("stdout", stdout), ("stderr", stderr)):
                    stream.seek(0, 2)
                    stream.seek(max(0, stream.tell() - 65536))
                    logs[name] = stream.read().decode("utf-8", errors="replace")
                if not timed_out and process.returncode == 0 and output.exists():
                    if output.stat().st_size > 2 * 1024 * 1024:
                        raise ValueError("Worker result exceeds size limit")
                    result = json.loads(output.read_text())
                    if (
                        result.get("status") != "ok"
                        or result.get("pass") != measurement_pass
                        or result.get("operations") != spec["operations"]
                    ):
                        raise ValueError("Invalid worker result identity")
                    return {**result, **logs}
                return {
                    "status": "timeout" if timed_out else "error",
                    "pass": measurement_pass,
                    "code": process.returncode,
                    "error": (
                        "Worker exceeded time limit"
                        if timed_out
                        else "Worker did not complete; exit alone does not establish OOM"
                    ),
                    **logs,
                }


def append_sample(benchmark, raw, repetition):
    benchmark["runs"].append({"repetition": repetition, **raw})
    if raw["status"] != "ok":
        benchmark["status"] = raw["status"]
        benchmark["reason"] = "One or more isolated workers failed; inspect raw runs."
        return
    definitions = {
        "elapsed_time_per_op": ("ns/op", "Stopwatch.GetTimestamp", "measured"),
        "process_cpu_time_per_op": ("ns/op", "Process.TotalProcessorTime", "measured"),
        "allocated_bytes_per_op": (
            "bytes/op",
            "GC.GetTotalAllocatedBytes(precise:true)",
            "measured",
        ),
        "idle_managed_heap_bytes": (
            "bytes",
            "GC.GetTotalMemory(false) after full GC",
            "estimated",
        ),
        "post_settling_managed_heap_bytes": (
            "bytes",
            "GC.GetTotalMemory(false) after full GC",
            "estimated",
        ),
        "idle_rss_bytes": ("bytes", "Process.WorkingSet64", "measured"),
        "post_settling_rss_bytes": (
            "bytes",
            "Process.WorkingSet64 after full GC",
            "measured",
        ),
        "observed_peak_rss_bytes": (
            "bytes",
            "Process.WorkingSet64 at operation checkpoints",
            "measured",
        ),
        "process_lifetime_peak_rss_bytes": (
            "bytes",
            "Process.PeakWorkingSet64 including startup and warmup",
            "measured",
        ),
    }
    for name, (unit, method, quality) in definitions.items():
        if name not in raw:
            continue
        value = raw[name]
        if (
            isinstance(value, bool)
            or not isinstance(value, (int, float))
            or not math.isfinite(value)
            or value < 0
        ):
            raise ValueError(f"Invalid {name}: {value}")
        metric = next((m for m in benchmark["metrics"] if m["id"] == name), None)
        if metric is None:
            metric = {
                "id": name,
                "unit": unit,
                "method": method,
                "quality": quality,
                "direction": "lower",
                "status": "ok",
                "samples": [],
                "scope": "Whole worker process; includes managed runtime and in-process offline transport work within the measured boundary.",
            }
            benchmark["metrics"].append(metric)
        metric["samples"].append(
            {
                "repetition": repetition,
                "value": value,
                "operations": raw["operations"],
                **({"elapsed_ns": raw["elapsed_ns"]} if "elapsed_ns" in raw else {}),
            }
        )
    if raw["pass"] == "memory" and "process_lifetime_peak_rss_bytes" not in raw:
        unavailable(
            benchmark,
            "process_lifetime_peak_rss_bytes",
            "bytes",
            "Runtime/OS did not expose a positive lifetime RSS peak.",
        )


def unavailable(benchmark, name, unit, reason):
    if not any(m["id"] == name for m in benchmark["metrics"]):
        benchmark["metrics"].append(
            {"id": name, "unit": unit, "status": "unsupported", "reason": reason}
        )


def run_pairs(documents, workers, workloads, repetitions, output, execute=run_worker):
    timeouts = 0
    failed = False
    for item in workloads:
        spec = item["spec"]
        for document in documents.values():
            benchmark = {
                "id": spec["id"],
                "fixture_digest": item["fixture_digest"],
                "parameters": spec,
                "status": "ok",
                "metrics": [],
                "runs": [],
            }
            if "allocation" in spec["passes"]:
                unavailable(
                    benchmark,
                    "allocations_per_op",
                    "count/op",
                    "CLR byte counters do not expose exact object allocation counts; native allocations are not included.",
                )
            document["benchmarks"].append(benchmark)
        for measurement_pass in spec["passes"]:
            for repetition in range(repetitions):
                for role in (
                    ["base", "head"] if repetition % 2 == 0 else ["head", "base"]
                ):
                    try:
                        raw = execute(workers[role], spec, measurement_pass)
                        if (
                            raw["status"] == "ok"
                            and raw.get("build_digest")
                            != documents[role]["execution"]["build_digest"]
                        ):
                            raise ValueError("Worker loaded a different SDK build")
                        benchmark = copy.deepcopy(documents[role]["benchmarks"][-1])
                        append_sample(benchmark, raw, repetition)
                        documents[role]["benchmarks"][-1] = benchmark
                    except (ValueError, OSError) as error:
                        raw = {
                            "status": "error",
                            "pass": measurement_pass,
                            "error": str(error)[:4096],
                        }
                        append_sample(
                            documents[role]["benchmarks"][-1], raw, repetition
                        )
                    failed |= raw["status"] != "ok"
                    timeouts += raw["status"] == "timeout"
                    (output / f"{role}.json").write_text(
                        json.dumps(documents[role], indent=2) + "\n"
                    )
                    print(
                        f"{spec['id']} {measurement_pass} {repetition} {role}: {raw['status']}",
                        flush=True,
                    )
                # Finish the pair, then stop; successful partial samples cannot hide failure.
                if timeouts >= MAX_TIMEOUTS:
                    return 1
    return int(failed)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True, type=Path)
    parser.add_argument("--head", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--build-directory", required=True, type=Path)
    parser.add_argument("--repetitions", type=int, default=6)
    parser.add_argument("--smoke", action="store_true")
    args = parser.parse_args()
    if not 1 <= args.repetitions <= 100:
        parser.error("repetitions must be between 1 and 100")
    args.output = args.output.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    pair_id = str(uuid.uuid4())
    suite_sources = b"".join(
        path.name.encode() + b"\0" + path.read_bytes()
        for path in sorted(HERE.iterdir())
        if path.suffix in (".cs", ".csproj", ".py", ".json")
    )
    suite = {
        "id": "posthog-dotnet-offline",
        "version": 1,
        "source_commit": git(HERE, "rev-parse", "HEAD"),
        "source_digest": digest(suite_sources),
        "profile": "smoke" if args.smoke else "ci",
        "repetitions": args.repetitions,
    }
    documents, workers, inventories = {}, {}, {}
    for role in ("base", "head"):
        source = getattr(args, role).resolve()
        worker = prepare(source, args.build_directory.resolve() / role)
        workers[role] = worker
        execution = describe(worker, args.smoke)
        inventories[role] = execution.pop("workloads")
        execution.update(
            {
                "pair_id": pair_id,
                "runtime_flags": [
                    "TieredCompilation=false",
                    "ServerGarbageCollection=false",
                    "GCHeapHardLimit=536870912",
                ],
                "cpu": cpu_model(),
                "runner_class": (
                    "github-hosted-or-self-hosted"
                    if os.getenv("RUNNER_NAME")
                    else "local"
                ),
                "runner_image": os.getenv("ImageVersion"),
                "dependency_lock_digest": digest(
                    (source / "src/PostHog/packages.lock.json").read_bytes()
                ),
                "packages": {
                    "application_dependency_graph_sha256": digest(
                        worker.with_suffix(".deps.json").read_bytes()
                    ),
                },
                "dotnet_sdk_version": subprocess.check_output(
                    ["dotnet", "--version"], cwd=HERE, text=True
                ).strip(),
                "resource_limits": {
                    "worker_timeout_ms": TIMEOUT * 1000,
                    "max_worker_timeouts": MAX_TIMEOUTS,
                    "managed_heap_limit_bytes": 512 * 1024 * 1024,
                },
            }
        )
        documents[role] = {
            "schema_version": 1,
            "sdk": "posthog-dotnet",
            "revision": {
                "role": role,
                "commit": git(source, "rev-parse", "HEAD"),
                "dirty": bool(git(source, "status", "--porcelain")),
            },
            "suite": suite,
            "execution": execution,
            "benchmarks": [],
        }
    if inventories["base"] != inventories["head"]:
        raise ValueError(
            "The two builds produced different fixture/workload identities"
        )
    subprocess.run(
        ["dotnet", str(workers["head"]), "controls"],
        check=True,
        env=environment(),
        timeout=TIMEOUT,
    )
    return run_pairs(
        documents, workers, inventories["head"], args.repetitions, args.output
    )


if __name__ == "__main__":
    raise SystemExit(main())
