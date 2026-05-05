using System.Threading;

namespace PostHog;

/// <summary>
/// Request-local PostHog context that is applied to captures in the current async execution flow.
/// </summary>
public sealed class PostHogContext
{
    static readonly AsyncLocal<PostHogContext?> CurrentContext = new();

    PostHogContext(
        string? distinctId,
        string? sessionId,
        IReadOnlyDictionary<string, object>? properties)
    {
        DistinctId = string.IsNullOrEmpty(distinctId) ? null : distinctId;
        SessionId = string.IsNullOrEmpty(sessionId) ? null : sessionId;
        Properties = properties?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<string, object>(0);
    }

    /// <summary>
    /// Gets the context for the current async execution flow, if any.
    /// </summary>
    public static PostHogContext? Current => CurrentContext.Value;

    /// <summary>
    /// The distinct ID applied to captures that do not pass one explicitly.
    /// </summary>
    public string? DistinctId { get; }

    /// <summary>
    /// The session ID applied as <c>$session_id</c> when captures do not provide one explicitly.
    /// </summary>
    public string? SessionId { get; }

    /// <summary>
    /// Properties applied to captures before explicit capture properties.
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties { get; }

    /// <summary>
    /// Begins a request-local context scope. Nested scopes inherit the current context unless <paramref name="fresh" /> is <c>true</c>.
    /// Dispose the returned value to restore the previous context.
    /// </summary>
    /// <param name="distinctId">Optional distinct ID to apply to captures in this scope.</param>
    /// <param name="sessionId">Optional session ID to apply as <c>$session_id</c> to captures in this scope.</param>
    /// <param name="properties">Optional properties to apply to captures in this scope.</param>
    /// <param name="fresh">If <c>true</c>, do not inherit values from the current context.</param>
    /// <returns>A disposable scope that restores the previous context.</returns>
    public static IDisposable BeginScope(
        string? distinctId = null,
        string? sessionId = null,
        IReadOnlyDictionary<string, object>? properties = null,
        bool fresh = false)
    {
        var parent = CurrentContext.Value;
        var baseContext = fresh ? null : parent;

        var mergedProperties = baseContext?.Properties.ToDictionary(pair => pair.Key, pair => pair.Value)
                               ?? new Dictionary<string, object>(0);
        if (properties is not null)
        {
            foreach (var (key, value) in properties)
            {
                mergedProperties[key] = value;
            }
        }

        var resolvedDistinctId = string.IsNullOrEmpty(distinctId) ? baseContext?.DistinctId : distinctId;
        var resolvedSessionId = string.IsNullOrEmpty(sessionId) ? baseContext?.SessionId : sessionId;

        CurrentContext.Value = new PostHogContext(resolvedDistinctId, resolvedSessionId, mergedProperties);
        return new DisposableScope(parent);
    }

    /// <summary>
    /// Sets the current async execution flow's context without a disposable scope.
    /// Prefer <see cref="BeginScope" /> when possible so the previous context is restored automatically.
    /// </summary>
    /// <param name="distinctId">Optional distinct ID to apply to captures.</param>
    /// <param name="sessionId">Optional session ID to apply as <c>$session_id</c> to captures.</param>
    /// <param name="properties">Optional properties to apply to captures.</param>
    /// <param name="fresh">If <c>true</c>, do not inherit values from the current context.</param>
    public static void Enter(
        string? distinctId = null,
        string? sessionId = null,
        IReadOnlyDictionary<string, object>? properties = null,
        bool fresh = false)
    {
        var current = CurrentContext.Value;
        var baseContext = fresh ? null : current;

        var mergedProperties = baseContext?.Properties.ToDictionary(pair => pair.Key, pair => pair.Value)
                               ?? new Dictionary<string, object>(0);
        if (properties is not null)
        {
            foreach (var (key, value) in properties)
            {
                mergedProperties[key] = value;
            }
        }

        var resolvedDistinctId = string.IsNullOrEmpty(distinctId) ? baseContext?.DistinctId : distinctId;
        var resolvedSessionId = string.IsNullOrEmpty(sessionId) ? baseContext?.SessionId : sessionId;

        CurrentContext.Value = new PostHogContext(resolvedDistinctId, resolvedSessionId, mergedProperties);
    }

    sealed class DisposableScope(PostHogContext? parent) : IDisposable
    {
        bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentContext.Value = parent;
            _disposed = true;
        }
    }
}
