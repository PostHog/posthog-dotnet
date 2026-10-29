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

    static void CheckInventory()
    {
        foreach (var smoke in new[] { false, true })
        {
            var workloads = Fixtures.Workloads(smoke);
            var expectedIds = new[] { "boolean", "rollout", "variant", "property", "regex", "cohort", "early", "late", "no-match" }
                .Select(kind => $"flags.single.{kind}")
                .Concat(new[] { "flags.all.10", "flags.all.100", "flags.all.1000", "flags.load.1000", "flags.refresh.1000",
                    "capture.enqueue.32", "capture.enqueue.4096", "capture.drain.steady.32", "capture.drain.steady.4096",
                    "capture.drain.burst.32", "capture.drain.burst.4096" }).ToArray();
            Program.Require(workloads.Select(w => w.Id).SequenceEqual(expectedIds), "Incorrect workload inventory");
            foreach (var spec in workloads)
            {
                var expectedOperations = spec.Mode switch
                {
                    "single" => smoke ? 160 : 16000,
                    "refresh" => smoke ? 4 : 40,
                    "all" when spec.Concurrency == 8 => smoke ? 32 : 1600,
                    "all" => smoke ? 16 : spec.FlagCount == 10 ? 1600 : 160,
                    _ => smoke ? 200 : 1000,
                };
                string[] expectedPasses = spec.Mode == "drain" ? ["timing", "allocation", "memory"]
                    : spec.Mode == "refresh" || spec.Concurrency == 8 ? ["memory"] : ["timing", "allocation"];
                Program.Require(spec.Operations == expectedOperations && spec.Passes.SequenceEqual(expectedPasses), "Incorrect operations/passes");
                Program.Require(spec.Concurrency == (spec.Id == "flags.load.1000" ? 8 : 1), "Incorrect concurrency");
                if (spec.Mode == "single")
                    Program.Require(spec.FlagCount == 10 && Fixtures.Cases[spec.FlagIndex] == spec.Id.Split('.')[2], "Incorrect single fixture");
                else if (spec.FlagCount > 0)
                    Program.Require(spec.FlagCount == int.Parse(spec.Id.Split('.')[2]), "Incorrect flag count");
                if (spec.PayloadBytes > 0)
                {
                    Program.Require(spec.PayloadBytes == int.Parse(spec.Id.Split('.')[^1]), "Incorrect payload size");
                    Program.Require(spec.CompletionBoundary == (spec.Mode == "capture" ? "api_return" : "completed_delivery"), "Incorrect completion boundary");
                    Program.Require(spec.FlushEvery == (spec.DrainStrategy == "steady" ? 100 : spec.Mode == "drain" ? spec.Operations : null), "Incorrect flush interval");
                    Program.Require(spec.DrainStrategy == (spec.Mode == "drain" ? spec.Id.Split('.')[2] : null), "Incorrect drain strategy");
                }
            }
        }
    }

    static JsonElement Event(int sequence, int payloadBytes = 32, string plan = "pro", string? user = null) =>
        JsonSerializer.SerializeToElement(new
        {
            @event = Fixtures.EventName,
            properties = new Dictionary<string, object>
            {
                ["distinct_id"] = user ?? Fixtures.Users[(sequence % 16 + 16) % 16],
                ["payload"] = new string('x', payloadBytes),
                ["plan"] = plan,
                ["sequence"] = sequence,
            },
        });

    static void Reject(Action action, string message)
    {
        var rejected = false;
        try { action(); }
        catch (InvalidOperationException) { rejected = true; }
        Program.Require(rejected, message);
    }

    static void CheckReceiver()
    {
        using var transport = new Transport(Fixtures.Workloads(true).Single(s => s.Id == "capture.enqueue.32"));
        transport.ResetDelivery(32);
        for (var i = 31; i >= 0; i--) transport.ValidateEvent(Event(i));
        Program.Require(transport.Delivered == 32, "Receiver must accept reordered delivery");
        transport.ResetDelivery(32);
        // Replacing sequence 16 with 0 preserves both total and per-user counts.
        for (var i = 0; i < 16; i++) transport.ValidateEvent(Event(i));
        Reject(() => transport.ValidateEvent(Event(0)), "Balanced duplicate/loss accepted");
        foreach (var invalid in new[] { Event(-1), Event(32), Event(16, plan: "free"), Event(16, payloadBytes: 31), Event(16, user: Fixtures.Users[1]) })
            Reject(() => transport.ValidateEvent(invalid), "Invalid capture fixture accepted");
        var properties = Fixtures.CaptureProperties(32, 32);
        properties[1]["sequence"] = 999;
        Program.Require((int)properties[0]["sequence"] == 0 && (int)properties[2]["sequence"] == 2, "Caller property dictionaries are shared");
    }

    static async Task CheckSchedulesAndRefresh()
    {
        foreach (var smoke in new[] { false, true })
        {
            foreach (var spec in Fixtures.Workloads(smoke).Where(s => s.Mode == "drain"))
            {
                await using var scenario = new Scenario(spec);
                await scenario.Prepare();
                Program.Require(scenario.Transport.Delivered == 0 && scenario.Transport.BatchRequests == 0 && scenario.ExplicitFlushes == 0,
                    "Warmup delivery phase did not reset");
                var flushes = new List<int>();
                var checkpoints = new List<int>();
                await scenario.Execute(spec.Operations, completed =>
                {
                    checkpoints.Add(completed);
                    Program.Require(scenario.ExplicitFlushes == (spec.DrainStrategy == "steady" ? completed / 100 : 0),
                        "Checkpoint on wrong side of explicit flush");
                }, flushes.Add);
                var expectedCheckpoints = Enumerable.Range(1, spec.Operations / 100).Select(i => i * 100).ToArray();
                Program.Require(checkpoints.SequenceEqual(expectedCheckpoints), "Incorrect capture checkpoints");
                Program.Require(flushes.SequenceEqual(spec.DrainStrategy == "steady" ? expectedCheckpoints : [spec.Operations]), "Incorrect explicit flush schedule");
                var count = scenario.ExplicitFlushes;
                await scenario.VerifyCompletion(spec.Operations);
                Program.Require(scenario.ExplicitFlushes == count, "Delivery verification performed a rescuing flush");
            }
        }
        await using var refresh = new Scenario(Fixtures.Workloads(true).Single(s => s.Mode == "refresh"));
        await refresh.Prepare();
        Program.Require(refresh.Transport.Generation == 0, "Measured flags did not start at generation zero");
        for (var generation = 1; generation <= 4; generation++)
        {
            var requests = refresh.Transport.DefinitionRequests;
            await refresh.Execute(1);
            Program.Require(refresh.Transport.Generation == generation && refresh.Transport.DefinitionRequests == requests + 1, "Incorrect reload generation/request count");
            await refresh.VerifyFlags();
        }
    }

    static async Task CheckFailedDelivery()
    {
        foreach (var strategy in new[] { "steady", "burst" })
        {
            await using var dropped = new Scenario(Fixtures.Workloads(true).Single(s => s.Id == $"capture.drain.{strategy}.32"));
            await dropped.Prepare();
            dropped.Transport.DropEvents = true;
            await dropped.Execute(dropped.Spec.Operations);
            dropped.Transport.DropEvents = false;
            var flushes = dropped.ExplicitFlushes;
            var rejected = false;
            try { await dropped.VerifyCompletion(dropped.Spec.Operations); }
            catch (InvalidOperationException) { rejected = true; }
            Program.Require(rejected && dropped.ExplicitFlushes == flushes, "Incomplete measured delivery rescued/accepted by verification");
        }
    }

    internal static async Task Run()
    {
        CheckFixtureIdentity();
        CheckInventory();
        CheckReceiver();
        await CheckSchedulesAndRefresh();
        await CheckFailedDelivery();
        foreach (var spec in Fixtures.Workloads(smoke: true))
        {
            foreach (var pass in spec.Passes)
            {
                // Each pass normally gets a fresh worker; use a fresh scenario here too.
                await using var measured = new Scenario(spec);
                Program.Require(measured.Options.FlushAt == 100000 && measured.Options.MaxQueueSize == 100001 &&
                    measured.Options.MaxBatchSize == 100000, "Incorrect controlled count limits");
                Program.Require(!measured.Options.EnableCompression && measured.Options.FlushInterval > TimeSpan.FromSeconds(120) &&
                    measured.Options.FeatureFlagPollInterval > TimeSpan.FromSeconds(120), "Incorrect compression or automatic polling/flush configuration");
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
                    var interval = spec.Mode == "refresh" ? 1 : spec.Mode == "drain" ? 100 : 32;
                    var expected = Enumerable.Range(1, spec.Operations).Where(i => i % interval == 0 || i == spec.Operations);
                    Program.Require(positions.SequenceEqual(expected), "Incorrect deterministic checkpoints");
                    Program.Require((long)result["post_settling_heap_used"] > 0, "Missing retained-heap signal");
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
        Console.WriteLine("20 workloads: inventory, fixture identity, receiver uniqueness, flush schedules, phase reset, refresh generations, checkpoints, failed delivery, allocation churn, retained heap and RSS controls passed.");
    }
}
