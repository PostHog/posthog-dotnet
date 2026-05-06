using System.Threading;
using PostHog.Api;

namespace PostHog;

/// <summary>
/// Request-local PostHog context that is applied to captures in the current async execution flow.
/// </summary>
internal sealed class PostHogContext
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

internal readonly record struct PostHogCaptureContext(
    string DistinctId,
    Dictionary<string, object>? Properties,
    bool IsPersonless);

internal static class PostHogContextHelper
{
    internal static PostHogCaptureContext ResolveCaptureContext(
        string? distinctId,
        Dictionary<string, object>? properties)
    {
        var postHogContext = PostHogContext.Current;
        var identity = ResolveIdentity(distinctId, postHogContext);
        var resolvedProperties = ApplyContextProperties(properties, postHogContext);
        if (identity.IsPersonless)
        {
            resolvedProperties ??= [];
            if (!resolvedProperties.ContainsKey(PostHogProperties.ProcessPersonProfile))
            {
                resolvedProperties[PostHogProperties.ProcessPersonProfile] = false;
            }
        }

        return new PostHogCaptureContext(identity.DistinctId, resolvedProperties, identity.IsPersonless);
    }

    internal static string? ResolveDistinctId(string? preferredDistinctId = null)
        => preferredDistinctId is not null ? preferredDistinctId : PostHogContext.Current?.DistinctId;

    internal static PostHogCaptureIdentity ResolveIdentity(
        string? distinctId,
        PostHogContext? context)
    {
        if (!string.IsNullOrEmpty(distinctId))
        {
            return new PostHogCaptureIdentity(distinctId!, IsPersonless: false);
        }

        var contextDistinctId = context?.DistinctId;
        if (!string.IsNullOrEmpty(contextDistinctId))
        {
            return new PostHogCaptureIdentity(contextDistinctId!, IsPersonless: false);
        }

        return new PostHogCaptureIdentity(Guid.NewGuid().ToString(), IsPersonless: true);
    }

    static Dictionary<string, object>? ApplyContextProperties(
        Dictionary<string, object>? properties,
        PostHogContext? context)
    {
        if (context is null)
        {
            return properties;
        }

        var mergedProperties = context.Properties.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (!string.IsNullOrEmpty(context.SessionId) && !mergedProperties.ContainsKey(PostHogProperties.SessionId))
        {
            mergedProperties[PostHogProperties.SessionId] = context.SessionId!;
        }

        if (properties is not null)
        {
            foreach (var (key, value) in properties)
            {
                mergedProperties[key] = value;
            }
        }

        return mergedProperties;
    }
}

internal readonly record struct PostHogCaptureIdentity(string DistinctId, bool IsPersonless);
