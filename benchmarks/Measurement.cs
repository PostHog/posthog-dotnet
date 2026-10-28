using System.Diagnostics;

namespace PostHog.Benchmarks;

internal static class Measurement
{
    internal static void Collect()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    internal static long Rss(Process process)
    {
        process.Refresh();
        var bytes = process.WorkingSet64;
        Program.Require(bytes > 0, "Current RSS is unavailable on this platform");
        return bytes;
    }

    internal static async Task<Dictionary<string, object>> Run(Scenario scenario, string pass)
    {
        await scenario.Prepare();
        var spec = scenario.Spec;
        var result = new Dictionary<string, object>
        {
            ["pass"] = pass, ["status"] = "ok", ["operations"] = spec.Operations,
            ["warmup_operations"] = scenario.WarmupOperations,
        };
        using var process = Process.GetCurrentProcess();
        if (pass == "timing")
        {
            var cpu = process.TotalProcessorTime;
            var start = Stopwatch.GetTimestamp();
            await scenario.Execute(spec.Operations);
            var elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
            result["elapsed_ns"] = elapsed;
            result["elapsed_time_per_op"] = elapsed / spec.Operations;
            result["process_cpu_time_per_op"] = (process.TotalProcessorTime - cpu).TotalNanoseconds / spec.Operations;
        }
        else if (pass == "allocation")
        {
            var before = GC.GetTotalAllocatedBytes(precise: true);
            await scenario.Execute(spec.Operations);
            result["allocated_bytes_per_op"] = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)spec.Operations;
        }
        else if (pass == "memory")
        {
            result["idle_managed_heap_bytes"] = GC.GetTotalMemory(forceFullCollection: false);
            result["idle_rss_bytes"] = Rss(process);
            var checkpoints = new List<object>(50);
            var peak = (long)result["idle_rss_bytes"];
            void Checkpoint(int completed)
            {
                var rss = Rss(process);
                peak = Math.Max(peak, rss);
                checkpoints.Add(new { operations = completed, rss_bytes = rss, managed_heap_bytes = GC.GetTotalMemory(false) });
            }
            await scenario.Execute(spec.Operations, Checkpoint);
            var completedRss = Rss(process);
            result["after_operations_rss_bytes"] = completedRss;
            result["observed_peak_rss_bytes"] = Math.Max(peak, completedRss);
            Collect();
            result["post_settling_managed_heap_bytes"] = GC.GetTotalMemory(false);
            result["post_settling_rss_bytes"] = Rss(process);
            var lifetime = process.PeakWorkingSet64;
            if (lifetime > 0) result["process_lifetime_peak_rss_bytes"] = lifetime;
            result["checkpoints"] = checkpoints;
        }
        else throw new ArgumentException($"Unknown pass: {pass}");

        // Correctness and delivery checks are outside the measurement boundary.
        if (spec.FlagCount > 0) await scenario.VerifyFlags();
        else
        {
            await scenario.Client.FlushAsync();
            scenario.VerifyDelivery(spec.Operations);
        }
        return result;
    }
}
