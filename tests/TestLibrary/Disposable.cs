namespace UnitTests.Library;

/// <summary>
/// Provides a set of static methods for creating Disposables.
/// This is based off of
/// https://docs.microsoft.com/en-us/previous-versions/dotnet/reactive-extensions/hh229792(v=vs.103)
/// </summary>
public static class Disposable
{
    public static readonly IDisposable Empty = new EmptyDisposable();

    /// <summary>
    /// Creates the disposable that invokes the specified action when disposed.
    /// </summary>
    /// <param name="onDispose">The action to run during IDisposable.Dispose.</param>
    /// <returns>The disposable object that runs the given action upon disposal.</returns>
    public static IDisposable Create(Action onDispose) => new ActionDisposable(onDispose);

    public static IDisposable Combine(params IDisposable?[] disposables) => new CombinedDisposable(disposables);

    sealed class CombinedDisposable : IDisposable
    {
        readonly IReadOnlyList<IDisposable?> _disposables;

        public CombinedDisposable(IReadOnlyList<IDisposable?> disposables)
        {
            _disposables = disposables;
        }

        public void Dispose()
        {
            for (var index = _disposables.Count - 1; index >= 0; index--)
            {
                var disposable = _disposables[index];
                disposable?.Dispose();
            }
        }
    }

    sealed class ActionDisposable : IDisposable
    {
        volatile Action? _onDispose;

        public ActionDisposable(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _onDispose, null)?.Invoke();
        }
    }

    sealed class EmptyDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}