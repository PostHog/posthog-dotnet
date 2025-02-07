using Microsoft.Extensions.Logging;
using UnitTests.Library;

public class FakeLogger : ILogger
{
    readonly string _categoryName;

    public IExternalScopeProvider? ScopeProvider { get; }
    public FakeLoggerProvider Provider { get; }

    public FakeLogger(string categoryName, FakeLoggerProvider provider)
    {
        _categoryName = categoryName;
        Provider = provider;
        ScopeProvider = provider;
    }

    public static FakeLogger<T> Create<T>() => new(new FakeLoggerProvider());

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var scopes = new List<object?>();

        // Collect all the scopes that are currently in... scope :)
        ScopeProvider?.ForEachScope(static (scope, list) => list.Add(scope), scopes);

        var evt = new LogEvent()
        {
            CategoryName = _categoryName,
            LogLevel = logLevel,
            EventId = eventId,
            Exception = exception,
            Message = formatter(state, exception),
            State = state,
            Scopes = scopes,
        };

        Provider.RecordEvent(evt);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
        ScopeProvider?.Push(state) ?? Disposable.Empty;
}

public class FakeLogger<T> : FakeLogger, ILogger<T>
{
    public FakeLogger(FakeLoggerProvider provider) : base(FakeLoggerProvider.GetCategoryName<T>(), provider)
    {
    }
}
