namespace PostHog.ErrorTracking;

/// <summary>
/// Describes the SDK-owned boundary that captured an exception: the mechanism category, whether the exception
/// escaped application code, the concrete hook that observed it, and its severity.
/// </summary>
/// <remarks>
/// This is the SDK's integration metadata channel. Integrations that only hold an <see cref="IPostHogClient"/>
/// (which may be a decorator around the real client) call <see cref="Enter"/> around their capture call, and the
/// client picks the metadata up when it captures that same exception instance.
/// </remarks>
internal sealed class ExceptionCaptureBoundary
{
    static readonly AsyncLocal<Scope?> Current = new();

    public ExceptionCaptureBoundary(string mechanismType, string source, bool handled, string level = "error")
    {
        MechanismType = mechanismType;
        Source = source;
        Handled = handled;
        Level = level;
    }

    /// <summary>The semantic capture category, serialized as the outermost <c>mechanism.type</c>.</summary>
    public string MechanismType { get; }

    /// <summary>The concrete integration or runtime hook, serialized as <c>$exception_source</c>.</summary>
    public string Source { get; }

    /// <summary>Whether application code consumed the exception at this boundary.</summary>
    public bool Handled { get; }

    /// <summary>The canonical <c>$exception_level</c>.</summary>
    public string Level { get; }

    /// <summary>
    /// Makes <paramref name="boundary"/> the ambient boundary for captures of <paramref name="exception"/> until
    /// the returned scope is disposed.
    /// </summary>
    public static BoundaryScope Enter(Exception exception, ExceptionCaptureBoundary boundary)
    {
        var previous = Current.Value;
        Current.Value = new Scope(exception, boundary);
        return new BoundaryScope(previous);
    }

    /// <summary>
    /// Returns the ambient boundary when it was entered for this exact exception instance, otherwise <c>null</c>.
    /// </summary>
    public static ExceptionCaptureBoundary? ForException(Exception exception)
    {
        var scope = Current.Value;
        return scope is not null && ReferenceEquals(scope.Exception, exception) ? scope.Boundary : null;
    }

    internal sealed class Scope(Exception exception, ExceptionCaptureBoundary boundary)
    {
        public Exception Exception { get; } = exception;
        public ExceptionCaptureBoundary Boundary { get; } = boundary;
    }

#pragma warning disable CA1034, CA1815 // Scope token for a using statement; never compared.
    internal readonly struct BoundaryScope : IDisposable
#pragma warning restore CA1034, CA1815
    {
        readonly Scope? _previous;

        internal BoundaryScope(Scope? previous) => _previous = previous;

        public void Dispose() => Current.Value = _previous;
    }
}
