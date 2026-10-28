using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PostHog.Benchmarks;

internal static class Controls
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void AllocateChurn()
    {
        for (var i = 0; i < 32; i++) GC.KeepAlive(new byte[1024 * 1024]);
    }

    sealed class ExtendedFlagOptions : FeatureFlagOptions
    {
        public bool UnrelatedDefault { get; init; }
    }

    static void CheckFixtureIdentity()
    {
        var spec = Fixtures.Workloads(smoke: true)[0];
        var baseline = Fixtures.FixtureDigest(spec);
        var extended = Fixtures.Options.Select(option => new ExtendedFlagOptions
        {
            PersonProperties = new(option.PersonProperties!),
            OnlyEvaluateLocally = option.OnlyEvaluateLocally,
            SendFeatureFlagEvents = option.SendFeatureFlagEvents,
        }).ToArray();
        using var sdkJson = JsonDocument.Parse(JsonSerializer.Serialize(extended, Program.Json));
        Program.Require(sdkJson.RootElement[0].TryGetProperty("unrelated_default", out _), "Extension control must change the serialized SDK schema");
        Program.Require(Fixtures.FixtureDigest(spec, extended) == baseline, "SDK schema extension changed explicit fixture identity");

        using var inputs = JsonDocument.Parse(Fixtures.FixtureInputs());
        string[] expectedKeys = ["distinct_id", "person_properties", "only_evaluate_locally", "send_feature_flag_events"];
        foreach (var input in inputs.RootElement.EnumerateArray())
        {
            Program.Require(input.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(expectedKeys.Order()),
                "Fixture identity must contain only benchmark-owned explicit inputs");
        }
        extended[0].PersonProperties!["plan"] = "changed-plan";
        Program.Require(Fixtures.FixtureDigest(spec, extended) != baseline, "Person-property change did not change fixture identity");
        Program.Require(Fixtures.FixtureDigest(spec with { FlagCount = spec.FlagCount + 1 }) != baseline,
            "Definition change did not change fixture identity");
    }

    internal static async Task Run()
    {
        CheckFixtureIdentity();
        foreach (var spec in Fixtures.Workloads(smoke: true))
        {
            foreach (var pass in spec.Passes)
            {
                // Each pass normally gets a fresh worker; use a fresh scenario here too.
                await using var measured = new Scenario(spec);
                var result = await Measurement.Run(measured, pass);
                Program.Require((string)result["status"] == "ok", "Control workload failed");
                if (pass == "allocation") Program.Require((double)result["allocated_bytes_per_op"] > 0, "Missing allocation signal");
                if (pass == "timing")
                {
                    Program.Require((double)result["elapsed_ns"] > 0, "Missing elapsed-time signal");
                    Program.Require((double)result["process_cpu_time_per_op"] >= 0, "Invalid CPU signal");
                }
                if (pass == "memory")
                {
                    var points = JsonSerializer.SerializeToElement(result["checkpoints"]);
                    var positions = points.EnumerateArray().Select(p => p.GetProperty("operations").GetInt32()).ToArray();
                    Program.Require(positions.SequenceEqual(positions.Distinct().Order()), "Checkpoint positions must increase strictly");
                    Program.Require(positions.Length > 0 && positions[^1] <= spec.Operations, "Invalid checkpoint operation count");
                    Program.Require((long)result["post_settling_managed_heap_bytes"] > 0, "Missing retained-heap signal");
                }
            }
        }
        Measurement.Collect();
        var before = GC.GetTotalAllocatedBytes(true);
        AllocateChurn();
        Measurement.Collect();
        Program.Require(GC.GetTotalAllocatedBytes(true) - before >= 32 * 1024 * 1024, "Allocation counter lost collected objects");
        var heap = GC.GetTotalMemory(false);
        var retained = new byte[32 * 1024 * 1024];
        Measurement.Collect();
        Program.Require(GC.GetTotalMemory(false) - heap >= retained.Length, "Retained-heap counter missed live data");
        GC.KeepAlive(retained);
        using var process = Process.GetCurrentProcess();
        Program.Require(Measurement.Rss(process) > 0, "RSS unavailable");

        await using var dropped = new Scenario(Fixtures.Workloads(true).Single(s => s.Id == "capture.enqueue.32"));
        await dropped.Prepare();
        dropped.Transport.DropEvents = true;
        await dropped.Execute(1);
        await dropped.Client.FlushAsync();
        var rejected = false;
        try { dropped.VerifyDelivery(1); }
        catch (InvalidOperationException) { rejected = true; }
        Program.Require(rejected, "Delivery control accepted missing events");
        Console.WriteLine("17 workloads, allocation churn, retained heap, RSS and delivery controls passed.");
    }
}
