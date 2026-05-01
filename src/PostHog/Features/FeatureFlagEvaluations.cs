using System.Collections.Concurrent;
using System.Text.Json;
using static PostHog.Library.Ensure;

namespace PostHog.Features;

/// <summary>
/// A point-in-time snapshot of feature flag evaluations for a single distinct id, returned by
/// <see cref="IPostHogClient"/>'s <c>EvaluateFlagsAsync</c> method. Reading flags from the snapshot
/// records access and lazily fires the <c>$feature_flag_called</c> event (deduplicated against the
/// SDK's per-distinct-id cache) so callers can branch on flags and then forward the snapshot to
/// <c>Capture(..., flags: snapshot, ...)</c> to attach <c>$feature/&lt;key&gt;</c> and
/// <c>$active_feature_flags</c> properties without a second <c>/flags</c> request.
/// </summary>
public sealed class FeatureFlagEvaluations
{
    readonly IFeatureFlagEvaluationsHost _host;
    readonly Dictionary<string, EvaluatedFlagRecord> _records;
    // Tracks which flags have been read via IsEnabled / GetFlag. Used as a set; the byte value is unused.
    // ConcurrentDictionary because the snapshot is a public type with no documented thread-safety
    // constraint, so callers may read it from parallel branches.
    readonly ConcurrentDictionary<string, byte> _accessed;
    readonly GroupCollection? _groups;
    readonly IReadOnlyCollection<string> _errors;

    internal FeatureFlagEvaluations(
        IFeatureFlagEvaluationsHost host,
        string distinctId,
        Dictionary<string, EvaluatedFlagRecord> records,
        string? requestId,
        long? evaluatedAt,
        long? flagDefinitionsLoadedAt,
        GroupCollection? groups,
        IReadOnlyCollection<string>? errors,
        ConcurrentDictionary<string, byte>? accessed = null)
    {
        _host = NotNull(host);
        DistinctId = distinctId ?? string.Empty;
        _records = records;
        RequestId = requestId;
        EvaluatedAt = evaluatedAt;
        FlagDefinitionsLoadedAt = flagDefinitionsLoadedAt;
        _groups = groups;
        _errors = errors ?? Array.Empty<string>();
        _accessed = accessed ?? new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The distinct id this snapshot was evaluated for. Empty when the snapshot was created
    /// as a safety fallback (e.g. an empty distinct id was passed to <c>EvaluateFlagsAsync</c>).
    /// </summary>
    public string DistinctId { get; }

    /// <summary>
    /// The request id reported by the <c>/flags</c> response, or <c>null</c> if the snapshot was
    /// fully resolved via local evaluation.
    /// </summary>
    public string? RequestId { get; }

    /// <summary>
    /// The timestamp (Unix milliseconds) reported by the <c>/flags</c> response, or <c>null</c>
    /// if the snapshot was fully resolved via local evaluation.
    /// </summary>
    public long? EvaluatedAt { get; }

    /// <summary>
    /// The Unix-millisecond timestamp at which the local flag definitions used by the snapshot
    /// were loaded, or <c>null</c> if no flag in the snapshot was locally evaluated.
    /// </summary>
    public long? FlagDefinitionsLoadedAt { get; }

    /// <summary>
    /// The set of flag keys present in this snapshot.
    /// </summary>
    public IReadOnlyCollection<string> Keys => _records.Keys;

    /// <summary>
    /// Returns <c>true</c> when the named flag is present in the snapshot and enabled. Records
    /// access on the snapshot and fires <c>$feature_flag_called</c> on first access for a given
    /// (distinct id, key, value) tuple.
    /// </summary>
    /// <param name="key">The feature flag key.</param>
    public bool IsEnabled(string key)
    {
        var record = RecordAccess(key);
        return record is { Enabled: true };
    }

    /// <summary>
    /// Returns the named flag from the snapshot, or <c>null</c> if it is not present. Records access
    /// on the snapshot and fires <c>$feature_flag_called</c> on first access for a given
    /// (distinct id, key, value) tuple.
    /// </summary>
    /// <param name="key">The feature flag key.</param>
    public FeatureFlag? GetFlag(string key)
    {
        var record = RecordAccess(key);
        return record?.Flag;
    }

    /// <summary>
    /// Returns the payload for the named flag, or <c>null</c> if it is not present or has no payload.
    /// Does NOT record access and does NOT fire <c>$feature_flag_called</c>.
    /// </summary>
    /// <param name="key">The feature flag key.</param>
    public JsonDocument? GetFlagPayload(string key)
        => _records.TryGetValue(NotNull(key), out var record) ? record.Flag.Payload : null;

    /// <summary>
    /// Returns a new snapshot containing only the flags that have been accessed via
    /// <see cref="IsEnabled"/> or <see cref="GetFlag"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fallback behavior:</b> if no flags have been accessed yet, this method logs a warning and
    /// returns a snapshot containing <i>all</i> flags. This avoids silently dropping exposure data
    /// when callers wire up <c>OnlyAccessed()</c> before any branching logic runs. Set
    /// <see cref="PostHogOptions.FeatureFlagsLogWarnings"/> to <c>false</c> to suppress the warning.
    /// </para>
    /// </remarks>
    public FeatureFlagEvaluations OnlyAccessed()
    {
        if (_accessed.IsEmpty)
        {
            _host.LogFilterWarning(
                "FeatureFlagEvaluations.OnlyAccessed() was called before any flags were accessed; " +
                "attaching all evaluated flags as a fallback.");
            return CloneWith(_records);
        }

        var filtered = new Dictionary<string, EvaluatedFlagRecord>(StringComparer.Ordinal);
        foreach (var key in _accessed.Keys)
        {
            if (_records.TryGetValue(key, out var record))
            {
                filtered[key] = record;
            }
        }
        return CloneWith(filtered);
    }

    /// <summary>
    /// Returns a new snapshot containing only the named flags. Unknown keys are dropped silently
    /// (a warning is logged for each missing key).
    /// </summary>
    /// <param name="keys">The flag keys to retain.</param>
    public FeatureFlagEvaluations Only(IEnumerable<string> keys)
    {
        var filtered = new Dictionary<string, EvaluatedFlagRecord>(StringComparer.Ordinal);
        List<string>? missing = null;
        foreach (var key in NotNull(keys))
        {
            if (_records.TryGetValue(key, out var record))
            {
                filtered[key] = record;
            }
            else
            {
                (missing ??= new List<string>()).Add(key);
            }
        }

        if (missing is { Count: > 0 })
        {
            _host.LogFilterWarning(
                "FeatureFlagEvaluations.Only(...) requested keys that are not in the snapshot and will be dropped: " +
                string.Join(", ", missing));
        }

        return CloneWith(filtered);
    }

    /// <inheritdoc cref="Only(System.Collections.Generic.IEnumerable{string})"/>
    public FeatureFlagEvaluations Only(params string[] keys)
        => Only((IEnumerable<string>)NotNull(keys));

    /// <summary>
    /// The internal per-flag records. Used by <see cref="PostHogClient"/>'s capture path to attach
    /// <c>$feature/&lt;key&gt;</c> properties. Exposed as <see cref="IReadOnlyDictionary{TKey,TValue}"/>
    /// so the caller cannot mutate the snapshot's underlying state.
    /// </summary>
    internal IReadOnlyDictionary<string, EvaluatedFlagRecord> Records => _records;

    /// <summary>
    /// Constructs an empty snapshot with no flags and no events. Used as the safety fallback when
    /// <c>EvaluateFlagsAsync</c> is called without a usable distinct id, or when remote evaluation
    /// is quota-limited.
    /// </summary>
    internal static FeatureFlagEvaluations Empty(IFeatureFlagEvaluationsHost host, string distinctId)
        => new(
            host,
            distinctId,
            new Dictionary<string, EvaluatedFlagRecord>(StringComparer.Ordinal),
            requestId: null,
            evaluatedAt: null,
            flagDefinitionsLoadedAt: null,
            groups: null,
            errors: null);

    EvaluatedFlagRecord? RecordAccess(string key)
    {
        var keyChecked = NotNull(key);
        var firstAccess = _accessed.TryAdd(keyChecked, 0);

        _records.TryGetValue(keyChecked, out var record);

        if (!firstAccess || string.IsNullOrEmpty(DistinctId))
        {
            // Repeat access in this snapshot, or the empty-distinct-id safety fallback: skip the
            // dedup-cache lookup and property allocation. Cross-snapshot dedup is still handled by
            // the per-distinct-id MemoryCache when the host runs.
            return record;
        }

        _host.CaptureFeatureFlagCalled(
            distinctId: DistinctId,
            featureKey: keyChecked,
            record: record,
            groups: _groups,
            requestId: RequestId,
            evaluatedAt: EvaluatedAt,
            flagDefinitionsLoadedAt: FlagDefinitionsLoadedAt,
            errors: _errors);

        return record;
    }

    FeatureFlagEvaluations CloneWith(Dictionary<string, EvaluatedFlagRecord> records)
        => new(
            _host,
            DistinctId,
            records,
            RequestId,
            EvaluatedAt,
            FlagDefinitionsLoadedAt,
            _groups,
            _errors,
            accessed: new ConcurrentDictionary<string, byte>(_accessed, StringComparer.Ordinal));
}
