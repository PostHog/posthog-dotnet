using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PostHog.Config;

namespace PostHog.Library;
internal sealed class AsyncBatchHandler<TItem> : IDisposable, IAsyncDisposable
{
    readonly Channel<TItem> _channel;
    readonly IOptions<AsyncBatchHandlerOptions> _options;
    readonly Func<IEnumerable<TItem>, Task> _batchHandlerFunc;
    readonly ILogger _logger;
    readonly PeriodicTimer _timer;
    readonly CancellationTokenSource _cancellationTokenSource = new();
    readonly SemaphoreSlim _flushSignal = new(0); // Used to signal when a flush is needed
    volatile int _disposed;
    volatile int _flushing;

    public AsyncBatchHandler(
        Func<IEnumerable<TItem>, Task> batchHandlerFunc,
        IOptions<AsyncBatchHandlerOptions> options,
        ITaskScheduler taskScheduler,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _batchHandlerFunc = batchHandlerFunc;
        _logger = logger;
        _channel = Channel.CreateBounded<TItem>(new BoundedChannelOptions(_options.Value.MaxQueueSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _timer = new PeriodicTimer(options.Value.FlushInterval, timeProvider);
        taskScheduler.Run(() => HandleTimer(_cancellationTokenSource.Token));
        taskScheduler.Run(() => HandleFlushSignal(_cancellationTokenSource.Token));
    }

    public AsyncBatchHandler(
        Func<IEnumerable<TItem>, Task> batchHandlerFunc,
        TimeProvider timeProvider,
        IOptions<AsyncBatchHandlerOptions> options)
        : this(batchHandlerFunc, options, new TaskRunTaskScheduler(), timeProvider, NullLogger.Instance)
    {
    }

    public int Count => _channel.Reader.Count;

    public void Enqueue(TItem item)
    {
        if (Count >= _options.Value.MaxQueueSize)
        {
            _logger.LogWarningMaxQueueSizeReached(_options.Value.MaxQueueSize, Count);
        }

        if (!_channel.Writer.TryWrite(item))
        {
            _logger.LogWarningCannotEnqueueEvent(_disposed is 1);
            return;
        }

        if (Count >= _options.Value.FlushAt)
        {
            _logger.LogTraceFlushCalledOnCaptureFlushAt(_options.Value.FlushAt, Count);
            // Signal that a flush is needed.
            SignalFlush();
        }
    }

    void SignalFlush()
    {
        if (_flushSignal.CurrentCount is 0)
        {
            _flushSignal.Release();
        }
    }

    async Task HandleFlushSignal(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _flushSignal.WaitAsync(cancellationToken);
                await FlushBatchesAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogTraceOperationCancelled(nameof(HandleFlushSignal));
        }
        catch (HttpRequestException ex) // TODO: Catch the exceptions we might expect.
        {
            _logger.LogErrorUnexpectedException(ex, nameof(HandleFlushSignal));
        }

    }

    async Task HandleTimer(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken))
            {
                if (Count > 0)
                {
                    _logger.LogTraceFlushCalledOnFlushInterval(_options.Value.FlushInterval, Count);
                    // Signal that a flush is needed.
                    SignalFlush();
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogTraceOperationCancelled(nameof(HandleTimer));
        }
    }

    public async Task FlushAsync()
    {
        _logger.LogInfoFlushCalledDirectly(Count);
        await FlushBatchesAsync();
    }

    async Task FlushBatchesAsync()
    {
        // If we're flushing, don't start another flush.
        if (Interlocked.CompareExchange(ref _flushing, 1, 0) == 1)
        {
            return;
        }

        var batch = new List<TItem>();
        try
        {
            while (_channel.Reader.TryRead(out var item))
            {
                batch.Add(item);
                if (batch.Count >= _options.Value.MaxBatchSize)
                {
                    // Batch is full, send it.
                    await SendBatch();
                    // Clear the batch.
                    batch.Clear();
                }
            }

            // Send any remaining items in the batch.
            if (batch.Count > 0)
            {
                await SendBatch();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
        }

        return;

        async Task SendBatch()
        {
            if (batch.Count is 0)
            {
                return;
            }

            _logger.LogDebugSendingBatch(batch.Count);
            await _batchHandlerFunc(batch);
            _logger.LogTraceBatchSent(Count);
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().Wait();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            _logger.LogWarningDisposeCalledTwice();
            return;
        }

        _logger.LogInfoDisposeAsyncCalled();

        // Ensures that both the HandleFlushSignal and HandleTimer throw
        // OperationCancelledException which is handled gracefully.
        await _cancellationTokenSource.CancelAsync();
        _cancellationTokenSource.Dispose();
        _timer.Dispose();
        _flushSignal.Dispose();
        _channel.Writer.Complete();
        try
        {
            _logger.LogTraceFlushCalledInDispose(Count);
            // Flush the last remaining items.
            await FlushBatchesAsync();
        }
        catch (HttpRequestException e)
        {
            _logger.LogErrorUnexpectedException(e, nameof(DisposeAsync));
        }
        catch (ObjectDisposedException e)
        {
            _logger.LogErrorUnexpectedException(e, nameof(DisposeAsync));
        }
    }
}

internal static partial class AsyncFlushingQueueLoggerExtensions
{
    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Debug,
        Message = "Sending Batch: {Count} items")]
    public static partial void LogDebugSendingBatch(this ILogger logger, int count);

    [LoggerMessage(
        EventId = 101,
        Level = LogLevel.Trace,
        Message = "Batch sent: Queue is now at {Count} items")]
    public static partial void LogTraceBatchSent(this ILogger logger, int count);

    [LoggerMessage(
        EventId = 102,
        Level = LogLevel.Trace,
        Message = "Flush called on capture because FlushAt ({FlushAt}) count met, {Count} items in the queue")]
    public static partial void LogTraceFlushCalledOnCaptureFlushAt(
        this ILogger logger, int flushAt, int count);

    [LoggerMessage(
        EventId = 103,
        Level = LogLevel.Trace,
        Message = "Flush called on the Flush Interval: {Interval}, {Count} items in the queue")]
    public static partial void LogTraceFlushCalledOnFlushInterval(
        this ILogger logger,
        TimeSpan interval,
        int count);

    [LoggerMessage(
        EventId = 104,
        Level = LogLevel.Trace,
        Message = "Flush called because we're disposing: {Count} items in the queue")]
    public static partial void LogTraceFlushCalledInDispose(
        this ILogger logger,
        int count);

    [LoggerMessage(
        EventId = 105,
        Level = LogLevel.Information,
        Message = "Flush called directly via code: {Count} items in the queue")]
    public static partial void LogInfoFlushCalledDirectly(this ILogger logger, int count);

    [LoggerMessage(
        EventId = 106,
        Level = LogLevel.Information,
        Message = "DisposeAsync called in AsyncBatchHandler")]
    public static partial void LogInfoDisposeAsyncCalled(this ILogger logger);

    [LoggerMessage(
        EventId = 107,
        Level = LogLevel.Warning,
        Message = "Cannot enqueue event. Disposed: {Disposed}")]
    public static partial void LogWarningCannotEnqueueEvent(
        this ILogger logger,
        bool disposed);

    [LoggerMessage(
        EventId = 108,
        Level = LogLevel.Warning,
        Message = "Dispose called a second time. Ignoring")]
    public static partial void LogWarningDisposeCalledTwice(this ILogger logger);

    [LoggerMessage(
        EventId = 109,
        Level = LogLevel.Error,
        Message = "Unexpected exception occurred in {MethodName}")]
    public static partial void LogErrorUnexpectedException(this ILogger logger, Exception e, string methodName);

    [LoggerMessage(
        EventId = 110,
        Level = LogLevel.Trace,
        Message = "{MethodName} exiting due to OperationCancelled exception")]
    public static partial void LogTraceOperationCancelled(
        this ILogger logger,
        string methodName);

    [LoggerMessage(
        EventId = 111,
        Level = LogLevel.Warning,
        Message = "MaxQueueSize ({MaxQueueSize}) reached. Count: {count}. Dropping oldest item.")]
    public static partial void LogWarningMaxQueueSizeReached(
        this ILogger logger,
        int maxQueueSize,
        int count);
}
