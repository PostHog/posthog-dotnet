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
    readonly bool[] _seen = new bool[Math.Max(100, spec.Operations)];
    readonly object _deliveryLock = new();
    int _expected;

    internal void ResetDelivery(int operations)
    {
        lock (_deliveryLock)
        {
            Program.Require(operations <= _seen.Length, "Receiver capacity exceeded");
            Array.Clear(_seen);
            _expected = operations;
            Delivered = BatchRequests = UnexpectedRequests = 0;
            Failure = null;
        }
    }

    internal void ValidateEvent(JsonElement item)
    {
        lock (_deliveryLock)
        {
            var properties = item.GetProperty("properties");
            var sequence = properties.GetProperty("sequence").GetInt32();
            Program.Require(item.GetProperty("event").GetString() == Fixtures.EventName, "Unexpected event");
            Program.Require(sequence >= 0 && sequence < _expected, "Sequence outside phase");
            Program.Require(properties.GetProperty("distinct_id").GetString() == Fixtures.Users[sequence % 16], "Wrong distinct ID");
            Program.Require(properties.GetProperty("payload").GetString() == _payload, "Wrong event payload");
            Program.Require(properties.GetProperty("plan").GetString() == "pro", "Wrong event plan");
            Program.Require(!_seen[sequence], "Duplicate sequence");
            if (!DropEvents)
            {
                _seen[sequence] = true;
                Delivered++;
            }
        }
    }
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
            Interlocked.Increment(ref BatchRequests);
            try
            {
                Program.Require(!request.Content!.Headers.ContentEncoding.Any(), "Compression must be disabled");
                await using var content = await request.Content.ReadAsStreamAsync(cancellationToken);
                using var body = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
                foreach (var item in body.RootElement.GetProperty("batch").EnumerateArray()) ValidateEvent(item);
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
    internal readonly PostHogOptions Options;
    internal readonly PostHogClient Client;
    Dictionary<string, object>[] _properties = [];
    readonly string[] _keys;
    internal long Checksum;
    internal int WarmupOperations;
    internal int ExplicitFlushes;

    internal Scenario(Workload spec)
    {
        Spec = spec;
        Transport = new(spec);
        Options = new PostHogOptions
        {
            ProjectToken = "benchmark-project-token",
            SecretKey = spec.FlagCount > 0 ? "benchmark-secret" : null,
            HostUrl = new Uri("http://benchmark.invalid"),
            FeatureFlagPollInterval = TimeSpan.FromHours(1),
            FlushInterval = TimeSpan.FromHours(1),
            FlushAt = 100000,
            MaxQueueSize = 100001,
            MaxBatchSize = 100000,
            EnableCompression = false,
        };
        Client = new(Options, httpClientFactory: Transport);
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
        if (Spec.PayloadBytes > 0) PrepareCapturePhase(WarmupOperations);
        await Execute(WarmupOperations);
        if (Spec.FlagCount > 0)
        {
            await VerifyFlags();
            Transport.Generation = 0;
            await Client.LoadFeatureFlagsAsync(CancellationToken.None);
            await VerifyFlags();
        }
        else
        {
            await VerifyCompletion(WarmupOperations);
            PrepareCapturePhase(Spec.Operations);
        }
        Checksum = 0;
        ExplicitFlushes = 0;
        await Task.Delay(25); // Let startup/background tasks reach their waits before measurement.
        Measurement.Collect();
    }

    internal void PrepareCapturePhase(int operations)
    {
        // Each call owns its properties; submitted dictionaries are never mutated or reused.
        _properties = Fixtures.CaptureProperties(Spec.PayloadBytes, operations);
        Transport.ResetDelivery(operations);
    }

    internal async Task Flush()
    {
        ExplicitFlushes++;
        await Client.FlushAsync();
    }

    internal async Task VerifyCompletion(int operations)
    {
        if (Spec.Mode == "capture") await Flush();
        VerifyDelivery(operations);
    }

    internal async Task Execute(int operations, Action<int>? checkpoint = null, Action<int>? flushed = null)
    {
        if (Spec.Mode is "capture" or "drain")
        {
            for (var i = 0; i < operations; i++)
            {
                Program.Require(Client.Capture(Fixtures.Users[i % 16], Fixtures.EventName, _properties[i]), "Capture rejected event");
                if (Spec.DrainStrategy == "steady" && ((i + 1) % Spec.FlushEvery == 0 || i + 1 == operations))
                {
                    await Flush();
                    flushed?.Invoke(i + 1);
                }
                if ((i + 1) % 100 == 0 || i + 1 == operations) checkpoint?.Invoke(i + 1);
            }
            if (Spec.DrainStrategy == "burst")
            {
                await Flush();
                flushed?.Invoke(operations);
            }
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
                        if (completed % 32 == 0 || completed == operations) checkpoint?.Invoke(completed);
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
