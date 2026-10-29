using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PostHog.Benchmarks;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static async Task<int> Main(string[] args)
    {
        try
        {
            if (args[0] == "describe")
            {
                var entry = typeof(PostHogClient).Assembly.Location;
                using var client = new PostHogClient(new PostHogOptions { ProjectToken = "benchmark-project-token", Disabled = true });
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    runtime = ".NET", runtime_version = Environment.Version.ToString(),
                    os = RuntimeInformation.OSDescription, arch = RuntimeInformation.ProcessArchitecture.ToString(),
                    kernel = Environment.OSVersion.VersionString, entry,
                    sdk_version = client.Version, build_digest = Fixtures.Digest(File.ReadAllBytes(entry)),
                    measurement_tools = new { clr = Environment.Version.ToString() },
                    workloads = Fixtures.Workloads(args.Contains("--smoke")).Select(spec => new
                    {
                        spec,
                        fixture_digest = Fixtures.FixtureDigest(spec),
                    }),
                }, Json));
            }
            else if (args[0] == "controls") await Controls.Run();
            else if (args[0] == "worker")
            {
                var spec = JsonSerializer.Deserialize<Workload>(File.ReadAllText(args[1]), Json)!;
                await using var scenario = new Scenario(spec);
                var result = await Measurement.Run(scenario, args[2]);
                var entry = typeof(PostHogClient).Assembly.Location;
                result["entry"] = entry;
                result["build_digest"] = Fixtures.Digest(File.ReadAllBytes(entry));
                await File.WriteAllTextAsync(args[3], JsonSerializer.Serialize(result, Json));
            }
            else throw new ArgumentException("Expected describe, controls or worker");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
