using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using PostHog.Features;

namespace PostHog.Benchmarks;

#pragma warning disable CS0618 // Measure supported legacy getters, matching the Node and Python suites.

// Public HTTP injection keeps this offline. Parsing/validation is part of the drain
// workload, not an estimate of socket or remote ingestion performance.
internal sealed class Transport(Workload spec) : HttpMessageHandler, IHttpClientFactory
{
    readonly string _payload = new('x', spec.PayloadBytes);
    internal int Generation;
    internal int DefinitionRequests;
    internal int BatchRequests;
    internal int Delivered;
    internal int UnexpectedRequests;
    internal string? Failure;
    internal bool DropEvents;

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.TrimEnd('/') == "/flags/definitions")
        {
            Interlocked.Increment(ref DefinitionRequests);
            return Response(Fixtures.Definitions(spec.FlagCount, Generation));
        }
        if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.TrimEnd('/') == "/batch")
        {
            BatchRequests++;
            try
            {
                await using var content = await request.Content!.ReadAsStreamAsync(cancellationToken);
                using var decoded = request.Content.Headers.ContentEncoding.Contains("gzip")
                    ? new GZipStream(content, CompressionMode.Decompress, leaveOpen: true)
                    : (Stream)content;
                using var body = await JsonDocument.ParseAsync(decoded, cancellationToken: cancellationToken);
                foreach (var item in body.RootElement.GetProperty("batch").EnumerateArray())
                {
                    var properties = item.GetProperty("properties");
                    Program.Require(item.GetProperty("event").GetString() == "benchmark-event", "Unexpected event");
                    Program.Require(properties.GetProperty("distinct_id").GetString() == "benchmark-user", "Wrong distinct ID");
                    Program.Require(properties.GetProperty("sequence").GetInt32() == Delivered, "Missing, duplicate or reordered event");
                    Program.Require(properties.GetProperty("payload").GetString() == _payload, "Wrong event payload");
                    if (!DropEvents) Delivered++;
                }
            }
            catch (Exception error)
            {
                Failure = error.Message;
                throw;
            }
            return Response("{\"status\":1}");
        }
        Interlocked.Increment(ref UnexpectedRequests);
        throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri?.AbsolutePath}");
    }

    static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}

internal sealed class Scenario : IAsyncDisposable
{
    internal readonly Workload Spec;
    internal readonly Transport Transport;
    internal readonly PostHogClient Client;
    readonly Dictionary<string, object> _properties;
    readonly string[] _keys;
    internal long Checksum;
    internal int WarmupOperations;

    internal Scenario(Workload spec)
    {
        Spec = spec;
        Transport = new(spec);
        Client = new(new PostHogOptions
        {
            ProjectToken = "benchmark-project-token",
            SecretKey = spec.FlagCount > 0 ? "benchmark-secret" : null,
            HostUrl = new Uri("http://benchmark.invalid"),
            FeatureFlagPollInterval = TimeSpan.FromHours(1),
            FlushInterval = TimeSpan.FromHours(1),
            FlushAt = 20001,
            MaxQueueSize = 20000,
            MaxBatchSize = 100,
        }, httpClientFactory: Transport);
        _properties = new() { ["payload"] = new string('x', spec.PayloadBytes), ["sequence"] = 0 };
        _keys = Enumerable.Range(0, spec.FlagCount).Select(Fixtures.Key).ToArray();
    }

    internal async Task Prepare()
    {
        if (Spec.FlagCount > 0)
        {
            await Client.LoadFeatureFlagsAsync(CancellationToken.None);
            await VerifyFlags();
        }
        WarmupOperations = Spec.Mode switch { "single" => 1024, "all" => 16, "refresh" => 2, _ => 100 };
        await Execute(WarmupOperations);
        if (Spec.FlagCount > 0)
        {
            Transport.Generation = 0;
            await Client.LoadFeatureFlagsAsync(CancellationToken.None);
        }
        else
        {
            await Client.FlushAsync();
            VerifyDelivery(WarmupOperations);
            Transport.Delivered = 0;
            Transport.BatchRequests = 0;
        }
        Checksum = 0;
        await Task.Delay(25); // Let startup/background tasks reach their waits before measurement.
        Measurement.Collect();
    }

    internal async Task Execute(int operations, Action<int>? checkpoint = null)
    {
        if (Spec.Mode is "capture" or "drain")
        {
            for (var i = 0; i < operations; i++)
            {
                _properties["sequence"] = i;
                Program.Require(Client.Capture("benchmark-user", "benchmark-event", _properties), "Capture rejected event");
                if ((i + 1) % Math.Max(1, operations / 20) == 0) checkpoint?.Invoke(i + 1);
            }
            Program.Require(Transport.BatchRequests == 0, "Queue flushed before explicit drain");
            if (Spec.Mode == "drain") await Client.FlushAsync();
            return;
        }
        if (Spec.Mode == "refresh")
        {
            for (var i = 0; i < operations; i++)
            {
                Transport.Generation++;
                var requests = Transport.DefinitionRequests;
                await Client.LoadFeatureFlagsAsync(CancellationToken.None);
                Program.Require(Transport.DefinitionRequests == requests + 1, "Reload did not fetch new definitions");
                VerifyFlag(await Client.GetFeatureFlagAsync(_keys[0], Fixtures.Users[0], Fixtures.Options[0]), 0, 0);
                VerifyFlag(await Client.GetFeatureFlagAsync(_keys[5], Fixtures.Users[0], Fixtures.Options[0]), 5, 0);
                checkpoint?.Invoke(i + 1);
            }
            return;
        }
        if (Spec.Concurrency > 1)
        {
            // Task.Run is deliberate: cached async getters can complete synchronously.
            // Merely collecting their Tasks would not produce overlapping evaluations.
            var completed = 0;
            var checkpointLock = new object();
            await Task.WhenAll(Enumerable.Range(0, Spec.Concurrency).Select(worker => Task.Run(async () =>
            {
                long consumed = 0;
                for (var i = worker; i < operations; i += Spec.Concurrency)
                {
                    consumed += await Evaluate(i);
                    lock (checkpointLock)
                    {
                        completed++;
                        if (completed % Math.Max(1, operations / 20) == 0) checkpoint?.Invoke(completed);
                    }
                }
                Interlocked.Add(ref Checksum, consumed);
            })));
        }
        else
        {
            for (var i = 0; i < operations; i++) Checksum += await Evaluate(i);
        }
        Program.Require(Checksum > 0, "Evaluation results were not consumed");
    }

    async ValueTask<int> Evaluate(int i)
    {
        var user = i % Fixtures.Users.Length;
        if (Spec.Mode == "single")
        {
            var value = await Client.GetFeatureFlagAsync(_keys[Spec.FlagIndex], Fixtures.Users[user], Fixtures.Options[user]);
            Program.Require(value is not null, "Missing local flag");
            return value!.VariantKey?.Length ?? (value.IsEnabled ? 2 : 1);
        }
        var flags = await Client.GetAllFeatureFlagsAsync(Fixtures.Users[user], Fixtures.Options[user]);
        Program.Require(flags.Count == Spec.FlagCount, "Missing local flags");
        return flags.Count;
    }

    void VerifyFlag(FeatureFlag? flag, int index, int user)
    {
        var expected = Fixtures.Expected(index, user, Transport.Generation);
        Program.Require(flag is not null && flag.Key == _keys[index] &&
            (expected is string variant ? flag.IsEnabled && flag.VariantKey == variant : flag.VariantKey is null && flag.IsEnabled == (bool)expected),
            $"Incorrect {_keys[index]} for user {user}, generation {Transport.Generation}");
    }

    internal async Task VerifyFlags()
    {
        for (var user = 0; user < Fixtures.Users.Length; user++)
        {
            var flags = await Client.GetAllFeatureFlagsAsync(Fixtures.Users[user], Fixtures.Options[user]);
            Program.Require(flags.Count == Spec.FlagCount,
                $"Wrong all-flags count: {flags.Count}/{Spec.FlagCount}; missing {string.Join(',', _keys.Except(flags.Keys))}; definition requests {Transport.DefinitionRequests}; unexpected requests {Transport.UnexpectedRequests}");
            for (var index = 0; index < Spec.FlagCount; index++) VerifyFlag(flags.GetValueOrDefault(_keys[index]), index, user);
            if (Spec.Mode == "single")
                VerifyFlag(await Client.GetFeatureFlagAsync(_keys[Spec.FlagIndex], Fixtures.Users[user], Fixtures.Options[user]), Spec.FlagIndex, user);
        }
        Program.Require(Transport.UnexpectedRequests == 0 && Transport.BatchRequests == 0, "Flags escaped local-only evaluation");
    }

    internal void VerifyDelivery(int operations) => Program.Require(
        Transport.Failure is null && Transport.UnexpectedRequests == 0 && Transport.Delivered == operations,
        $"Delivery mismatch: expected {operations}, received {Transport.Delivered}; {Transport.Failure}");

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        Transport.Dispose();
    }
}
