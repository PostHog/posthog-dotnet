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
    readonly IReadOnlyDictionary<string, EvaluatedFlagRecord> _records;
    readonly HashSet<string> _accessed;
    readonly GroupCollection? _groups;
    readonly IReadOnlyCollection<string> _errors;

    internal FeatureFlagEvaluations(
        IFeatureFlagEvaluationsHost host,
        string distinctId,
        IReadOnlyDictionary<string, EvaluatedFlagRecord> records,
        string? requestId,
        long? evaluatedAt,
        long? flagDefinitionsLoadedAt,
        GroupCollection? groups,
        IReadOnlyCollection<string>? errors,
        HashSet<string>? accessed = null)
    {
        _host = NotNull(host);
        DistinctId = distinctId ?? string.Empty;
        _records = records;
        RequestId = requestId;
        EvaluatedAt = evaluatedAt;
        FlagDefinitionsLoadedAt = flagDefinitionsLoadedAt;
        _groups = groups;
        _errors = errors ?? Array.Empty<string>();
        _accessed = accessed ?? new HashSet<string>(StringComparer.Ordinal);
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
    public IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)_records.Keys;

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
    /// <see cref="IsEnabled"/> or <see cref="GetFlag"/>. If no flags have been accessed yet,
    /// logs a warning and returns a snapshot containing all flags so callers do not silently
    /// drop exposure data.
    /// </summary>
    public FeatureFlagEvaluations OnlyAccessed()
    {
        if (_accessed.Count == 0)
        {
            _host.LogFilterWarning(
                "FeatureFlagEvaluations.OnlyAccessed() was called before any flags were accessed; " +
                "attaching all evaluated flags as a fallback.");
            return CloneWith(_records);
        }

        var filtered = new Dictionary<string, EvaluatedFlagRecord>(StringComparer.Ordinal);
        foreach (var key in _accessed)
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
        var missing = new List<string>();
        foreach (var key in NotNull(keys))
        {
            if (_records.TryGetValue(key, out var record))
            {
                filtered[key] = record;
            }
            else
            {
                missing.Add(key);
            }
        }

        if (missing.Count > 0)
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
    /// <c>$feature/&lt;key&gt;</c> properties.
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
        _accessed.Add(keyChecked);

        if (string.IsNullOrEmpty(DistinctId))
        {
            // Empty-distinct-id snapshots are a safety fallback. Do not emit $feature_flag_called
            // events with an empty distinct id, since they would pollute analytics.
            return _records.TryGetValue(keyChecked, out var emptyRecord) ? emptyRecord : null;
        }

        _records.TryGetValue(keyChecked, out var record);

        _host.TryCaptureFeatureFlagCalledEventIfNeeded(
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

    FeatureFlagEvaluations CloneWith(IReadOnlyDictionary<string, EvaluatedFlagRecord> records)
        => new(
            _host,
            DistinctId,
            records,
            RequestId,
            EvaluatedAt,
            FlagDefinitionsLoadedAt,
            _groups,
            _errors,
            accessed: new HashSet<string>(_accessed, StringComparer.Ordinal));
}
