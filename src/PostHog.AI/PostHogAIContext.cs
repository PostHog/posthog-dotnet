using System;
using System.Collections.Generic;
using System.Threading;

namespace PostHog.AI;

/// <summary>
/// Represents the context for PostHog AI tracking, allowing customization of events within a scope.
/// </summary>
public sealed class PostHogAIContext
{
    private static readonly AsyncLocal<PostHogAIContext?> _current = new();

    /// <summary>
    /// Gets the current PostHog AI context.
    /// </summary>
    public static PostHogAIContext? Current => _current.Value;

    /// <summary>
    /// The distinct ID of the user.
    /// </summary>
    public string? DistinctId { get; }

    /// <summary>
    /// The trace ID for grouping events.
    /// </summary>
    public string? TraceId { get; }

    /// <summary>
    /// The session ID to group related traces together.
    /// </summary>
    public string? SessionId { get; }

    /// <summary>
    /// The span ID for this generation event.
    /// </summary>
    public string? SpanId { get; }

    /// <summary>
    /// The name for this generation span.
    /// </summary>
    public string? SpanName { get; }

    /// <summary>
    /// The parent span ID for tree view grouping.
    /// </summary>
    public string? ParentId { get; }

    /// <summary>
    /// Additional properties to include in the event.
    /// </summary>
    public Dictionary<string, object>? Properties { get; }

    /// <summary>
    /// Groups to associate with the event.
    /// </summary>
    public Dictionary<string, object>? Groups { get; }

    /// <summary>
    /// Whether privacy mode is enabled (e.g., to mask PII).
    /// </summary>
    public bool? PrivacyMode { get; }

    private PostHogAIContext(
        string? distinctId,
        string? traceId,
        string? sessionId,
        string? spanId,
        string? spanName,
        string? parentId,
        Dictionary<string, object>? properties,
        Dictionary<string, object>? groups,
        bool? privacyMode
    )
    {
        DistinctId = distinctId;
        TraceId = traceId;
        SessionId = sessionId;
        SpanId = spanId;
        SpanName = spanName;
        ParentId = parentId;
        Properties = properties;
        Groups = groups;
        PrivacyMode = privacyMode;
    }

    /// <summary>
    /// Begins a new PostHog AI tracking scope.
    /// </summary>
    /// <param name="distinctId">Optional distinct ID to associate with events in this scope.</param>
    /// <param name="traceId">Optional trace ID to group events.</param>
    /// <param name="sessionId">Optional session ID to group related traces together.</param>
    /// <param name="spanId">Optional span ID for this generation event.</param>
    /// <param name="spanName">Optional name for this generation span.</param>
    /// <param name="parentId">Optional parent span ID for tree view grouping.</param>
    /// <param name="properties">Optional properties to add to events.</param>
    /// <param name="groups">Optional groups to associate with events.</param>
    /// <param name="privacyMode">Optional flag to enable/disable privacy mode.</param>
    /// <returns>An <see cref="IDisposable"/> that ends the scope when disposed.</returns>
    public static IDisposable BeginScope(
        string? distinctId = null,
        string? traceId = null,
        string? sessionId = null,
        string? spanId = null,
        string? spanName = null,
        string? parentId = null,
        Dictionary<string, object>? properties = null,
        Dictionary<string, object>? groups = null,
        bool? privacyMode = null
    )
    {
        // Capture the parent context to restore it later
        var parent = _current.Value;

        // Create the new context, merging with parent if desired (optional complexity).
        // For simplicity and typical "scope" behavior, we can treat this as an override/overlay.
        // If we wanted to inherit parent properties, we'd do that merge here.
        // Let's stick to "current scope overrides" for now, but if a value is null, we *could* fallback to parent.
        // However, explicit "BeginScope" usually implies "Here is the context for this block".
        // Let's keep it simple: New scope = New context values.

        var newContext = new PostHogAIContext(
            distinctId ?? parent?.DistinctId,
            traceId ?? parent?.TraceId,
            sessionId ?? parent?.SessionId,
            spanId ?? parent?.SpanId,
            spanName ?? parent?.SpanName,
            parentId ?? parent?.ParentId,
            MergeDictionaries(parent?.Properties, properties),
            MergeDictionaries(parent?.Groups, groups),
            privacyMode ?? parent?.PrivacyMode
        );

        _current.Value = newContext;

        return new DisposableScope(parent);
    }

    private static Dictionary<string, object>? MergeDictionaries(
        Dictionary<string, object>? parent,
        Dictionary<string, object>? child
    )
    {
        if (parent == null && child == null)
            return null;
        if (parent == null)
            return child;
        if (child == null)
            return parent;

        var merged = new Dictionary<string, object>(parent);
        foreach (var kvp in child)
        {
            merged[kvp.Key] = kvp.Value;
        }
        return merged;
    }

    private sealed class DisposableScope : IDisposable
    {
        private readonly PostHogAIContext? _parent;
        private bool _disposed;

        public DisposableScope(PostHogAIContext? parent)
        {
            _parent = parent;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _current.Value = _parent;
            _disposed = true;
        }
    }
}
