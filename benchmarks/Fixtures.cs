using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PostHog.Benchmarks;

internal record Workload(string Id, string Mode, int Operations, string[] Passes,
    int FlagCount = 0, int FlagIndex = 0, int Concurrency = 1, int PayloadBytes = 0,
    string? DrainStrategy = null, int? FlushEvery = null, string? CompletionBoundary = null);

internal static class Fixtures
{
    internal const string EventName = "benchmark";
    internal static readonly string[] Cases =
        ["boolean", "rollout", "variant", "property", "regex", "cohort", "early", "late", "no-match", "inactive"];
    internal static readonly string[] Users = Enumerable.Range(0, 16).Select(i => $"benchmark-user-{i}").ToArray();
    internal static readonly FeatureFlagOptions[] Options = Enumerable.Range(0, 16).Select(i => new FeatureFlagOptions
    {
        OnlyEvaluateLocally = true,
        SendFeatureFlagEvents = false,
        PersonProperties = new() { ["plan"] = i % 2 == 0 ? "pro" : "free", ["email"] = $"user-{i}@example.test", ["score"] = i },
    }).ToArray();

    internal static string Key(int i) => $"{Cases[i % Cases.Length]}-{i}";
    internal static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string Digest(string value) => Digest(Encoding.UTF8.GetBytes(value));
    internal static string FixtureInputs(IEnumerable<FeatureFlagOptions>? options = null) => JsonSerializer.Serialize(
        (options ?? Options).Select((option, i) => new
        {
            DistinctId = Users[i],
            option.PersonProperties,
            option.OnlyEvaluateLocally,
            option.SendFeatureFlagEvents,
        }), Program.Json);

    internal static string FixtureDigest(Workload spec, IEnumerable<FeatureFlagOptions>? options = null) =>
        Digest(Definitions(spec.FlagCount) + FixtureInputs(options) + JsonSerializer.Serialize(spec, Program.Json) +
            (spec.PayloadBytes > 0 ? EventName + JsonSerializer.Serialize(CaptureProperties(spec.PayloadBytes, spec.Operations), Program.Json) : ""));

    internal static Dictionary<string, object>[] CaptureProperties(int payloadBytes, int operations)
    {
        var payload = new string('x', payloadBytes);
        return Enumerable.Range(0, operations).Select(i => new Dictionary<string, object>
        {
            ["payload"] = payload,
            ["plan"] = "pro",
            ["sequence"] = i,
        }).ToArray();
    }

    static object Property(string key, object value, string op = "exact") => new { key, value, @operator = op, type = "person" };
    static object Group(object[] properties, int rollout = 100) => new { properties, rollout_percentage = rollout };

    internal static string Definitions(int count, int generation = 0)
    {
        var flags = Enumerable.Range(0, count).Select(i =>
        {
            var kind = Cases[i % Cases.Length];
            var match = Group([Property("plan", new[] { "pro" })]);
            var miss = Group([Property("plan", new[] { "never" })]);
            object[] groups = kind switch
            {
                "rollout" => [Group([], 50)],
                "property" => [match],
                "regex" => [Group([Property("email", $"^(user|refresh-{generation})-[0-9]+@example[.]test$", "regex")])],
                "cohort" => [Group([new { key = "id", value = 1, type = "cohort" }])],
                "early" => [match, .. Enumerable.Repeat(miss, 7)],
                "late" => [.. Enumerable.Repeat(miss, 7), match],
                "no-match" => Enumerable.Repeat(miss, 8).ToArray(),
                _ => [Group([])],
            };
            var filters = new Dictionary<string, object> { ["groups"] = groups };
            if (kind == "variant")
                filters["multivariate"] = new { variants = new[] { new { key = "control", rollout_percentage = 50 }, new { key = "test", rollout_percentage = 50 } } };
            return new { id = i + 1, key = Key(i), name = Key(i), active = kind != "inactive" && (kind != "boolean" || generation % 2 == 0), filters };
        }).ToArray();
        return JsonSerializer.Serialize(new
        {
            flags,
            group_type_mapping = new Dictionary<string, string>(),
            cohorts = new Dictionary<string, object>
            {
                ["1"] = new { type = "AND", values = new[] { Property("plan", new[] { "pro" }), Property("score", generation % 2 == 0 ? -1 : 100, "gt") } },
            },
        });
    }

    // Independent fixture oracle: no SDK evaluator or private SDK state is used.
    static double Bucket(string key, string user, string salt = "")
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{key}.{user}{salt}")));
        return long.Parse(hash[..15], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / Math.Pow(2, 60);
    }

    internal static object Expected(int index, int user, int generation = 0) => Cases[index % Cases.Length] switch
    {
        "rollout" => Bucket(Key(index), Users[user]) < 0.5,
        "variant" => Bucket(Key(index), Users[user], "variant") < 0.5 ? "control" : "test",
        "boolean" => generation % 2 == 0,
        "cohort" => generation % 2 == 0 && user % 2 == 0,
        "property" or "early" or "late" => user % 2 == 0,
        "no-match" or "inactive" => false,
        _ => true,
    };

    internal static Workload[] Workloads(bool smoke) =>
    [
        .. Cases.Take(9).Select((kind, i) => new Workload($"flags.single.{kind}", "single", smoke ? 160 : 16000, ["timing", "allocation"], 10, i)),
        .. new[] { 10, 100, 1000 }.Select(count => new Workload($"flags.all.{count}", "all", smoke ? 16 : Math.Max(160, 16000 / count), ["timing", "allocation"], count)),
        new("flags.load.1000", "all", smoke ? 32 : 1600, ["memory"], 1000, Concurrency: 8),
        new("flags.refresh.1000", "refresh", smoke ? 4 : 40, ["memory"], 1000),
        .. new[] { 32, 4096 }.Select(size => new Workload($"capture.enqueue.{size}", "capture", smoke ? 200 : 1000,
            ["timing", "allocation"], PayloadBytes: size, CompletionBoundary: "api_return")),
        .. new[] { "steady", "burst" }.SelectMany(strategy => new[] { 32, 4096 }.Select(size =>
            new Workload($"capture.drain.{strategy}.{size}", "drain", smoke ? 200 : 1000,
                ["timing", "allocation", "memory"], PayloadBytes: size, DrainStrategy: strategy,
                FlushEvery: strategy == "steady" ? 100 : smoke ? 200 : 1000, CompletionBoundary: "completed_delivery"))),
    ];
}
