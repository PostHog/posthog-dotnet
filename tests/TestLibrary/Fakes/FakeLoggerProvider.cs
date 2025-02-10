

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

public sealed class FakeLoggerProvider : ILoggerProvider, ILoggerFactory, IExternalScopeProvider
{
    // I don't think locking is a problem here. Contention is low.
    // If it becomes a problem, we could use ConcurrentQueue or ImmutableQueue instead.
    readonly object _lock = new();
    readonly List<LogEvent> _allEvents = new();

    readonly AsyncLocal<Scope?> _scope = new();
    readonly ITestOutputHelper? _testOutputHelper;

    public ConcurrentDictionary<string, FakeLogger> Loggers { get; } = new();

    public FakeLoggerProvider(IServiceProvider? serviceProvider = null)
    {
        _testOutputHelper = serviceProvider?.GetService<ITestOutputHelper>();
    }

    public static string GetCategoryName<T>() =>
        // Copied from: https://github.com/dotnet/runtime/blob/8b1d1eabe32ba781ffcce2867333dfdc53bdd635/src/libraries/Microsoft.Extensions.Logging.Abstractions/src/LoggerT.cs
        TypeNameHelper.GetTypeDisplayName(typeof(T), includeGenericParameters: false, nestedTypeDelimiter: '.');

    public void Dispose()
    {
    }

    /// <summary>
    /// Fetches all logs from categories that match the category prefix specified by <paramref name="categoryPrefix"/>.
    /// </summary>
    /// <param name="categoryPrefix">
    /// The category prefix to match.
    /// The default value if this is not specified is "Serious.".
    /// Specify 'null' explicitly to match all categories.
    /// </param>
    /// <param name="minimumLevel">The minimum <see cref="LogLevel"/> of events to retrieve.</param>
    /// <param name="eventName">If specified, only events matching this name will be retrieved.</param>
    public IReadOnlyList<LogEvent> GetAllEvents(string? categoryPrefix = "Serious.", LogLevel? minimumLevel = null, string? eventName = null)
    {
        List<LogEvent> destination;
        lock (_allEvents)
        {
            destination = _allEvents
                .Where(e => categoryPrefix is null || (e.CategoryName ?? string.Empty).StartsWith(categoryPrefix, StringComparison.Ordinal))
                .Where(e => eventName is null || e.EventId.Name == eventName)
                .Where(e => minimumLevel is null || e.LogLevel >= minimumLevel.Value)
                .ToList();
        }
        return destination;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return Loggers.GetOrAdd(categoryName, _ => new FakeLogger(categoryName, this));
    }

    internal void RecordEvent(LogEvent evt)
    {
        lock (_lock)
        {
            _allEvents.Add(evt);
            if (_testOutputHelper is not null)
            {
                _testOutputHelper.WriteLine($"[{evt.CategoryName}:{evt.EventId.Name}] [{evt.LogLevel}] {evt.Message}");
            }
        }
    }

    public bool DidLog<T>(string eventName, IReadOnlyDictionary<string, object> parameters) =>
        DidLog(GetCategoryName<T>(), eventName, parameters);

    public bool DidLog(string categoryName, string eventName, IReadOnlyDictionary<string, object> parameters)
    {
        bool StateMatches(object? argState, IReadOnlyDictionary<string, object> expectedParameters)
        {
            if (argState is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                var argDict = new Dictionary<string, object>(pairs);
                foreach (var (expectedKey, expectedValue) in expectedParameters)
                {
                    if (!argDict.TryGetValue(expectedKey, out var actualValue)
                        || !Equals(actualValue, expectedValue))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        var evts = GetAllEvents(categoryName);
        return evts.Any(msg => msg.EventId.Name == eventName && StateMatches(msg.State, parameters));
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
    {
        var scopes = new List<Scope>();

        var current = _scope.Value;
        while (current != null)
        {
            scopes.Add(current);
            current = current.Parent;
        }

        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            callback(scopes[i].State, state);
        }
    }

    public IDisposable Push(object? state)
    {
        var current = _scope.Value;
        var next = new Scope(this, state, current);
        _scope.Value = next;
        return next;
    }

    sealed record Scope(FakeLoggerProvider LoggerProvider, object? State, Scope? Parent) : IDisposable
    {
        bool _disposed;
        public void Dispose()
        {
            if (!_disposed)
            {
                LoggerProvider._scope.Value = Parent;
                _disposed = true;
            }
        }
    }
}

public class LogEvent
{
    public string? CategoryName { get; init; }
    public LogLevel LogLevel { get; init; }
    public EventId EventId { get; init; }
    public Exception? Exception { get; init; }
    public string? Message { get; init; }
    public object? State { get; init; }
    public IReadOnlyList<object?>? Scopes { get; init; }
}