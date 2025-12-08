using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static PostHog.Library.Ensure;

namespace PostHog.Library;

/// <summary>
/// Represents a batch item that can be fetched asynchronously.
/// </summary>
/// <param name="itemFetcher">Used to fetch the item to send in the batch.</param>
/// <typeparam name="TItem">The type of the item.</typeparam>
/// <typeparam name="TBatchContext">
/// The type of the context object to pass to batch items. A new instance is passed for each batch.
/// </typeparam>
internal class BatchItem<TItem, TBatchContext>(Func<TBatchContext, Task<TItem>> itemFetcher)
{
    /// <summary>
    /// Resolves the item to send in the batch.
    /// </summary>
    /// <param name="context">Context to provide to the resolver.</param>
    /// <returns>The item to send in the batch.</returns>
    public Task<TItem> ResolveItem(TBatchContext context) => itemFetcher(context);
}

/// <summary>
/// Allows enqueueing items and flushing them in batches. Flushes happen on a periodic basis or when the queue reaches
/// a certain size (<see cref="PostHogOptions.FlushAt"/>).
/// </summary>
/// <typeparam name="TItem">The type of item to batch.</typeparam>
/// <typeparam name="TBatchContext">
/// The type of the context object to pass to batch items. A new instance is passed for each batch.
/// </typeparam>
internal sealed class AsyncBatchHandler<TItem, TBatchContext> : IDisposable, IAsyncDisposable
{
    readonly Channel<BatchItem<TItem, TBatchContext>> _channel;
    readonly IOptions<PostHogOptions> _options;
    readonly Func<IEnumerable<TItem>, Task> _batchHandlerFunc;
    readonly Func<TBatchContext> _batchContextFunc;
    readonly ILogger<AsyncBatchHandler> _logger;
    readonly PeriodicTimer _timer;
    readonly CancellationTokenSource _cancellationTokenSource = new();
    readonly SemaphoreSlim _flushSignal = new(0); // Used to signal when a flush is needed
    volatile int _disposed;
    volatile int _flushing;

    public AsyncBatchHandler(
        Func<IEnumerable<TItem>, Task> batchHandlerFunc,
        Func<TBatchContext> batchContextFunc,
        IOptions<PostHogOptions> options,
        ITaskScheduler taskScheduler,
        TimeProvider timeProvider,
        ILogger<AsyncBatchHandler> logger
    )
    {
        _options = NotNull(options);
        _batchHandlerFunc = batchHandlerFunc;
        _batchContextFunc = batchContextFunc;
        _logger = logger;
        _channel = Channel.CreateBounded<BatchItem<TItem, TBatchContext>>(
            new BoundedChannelOptions(_options.Value.MaxQueueSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
            }
        );
        _timer = new PeriodicTimer(options.Value.FlushInterval, timeProvider);
        taskScheduler.Run(() => HandleTimer(_cancellationTokenSource.Token));
        taskScheduler.Run(() => HandleFlushSignal(_cancellationTokenSource.Token));
    }

    public AsyncBatchHandler(
        Func<IEnumerable<TItem>, Task> batchHandlerFunc,
        Func<TBatchContext> batchContextFunc,
        TimeProvider timeProvider,
        IOptions<PostHogOptions> options
    )
        : this(
            batchHandlerFunc,
            batchContextFunc,
            options,
            new TaskRunTaskScheduler(),
            timeProvider,
            NullLogger<AsyncBatchHandler>.Instance
        ) { }

    public int Count => _channel.Reader.Count;

    /// <summary>
    /// Enqueues an item and returns true if the item was successfully enqueued.
    /// </summary>
    /// <param name="item">The item to enqueue</param>
    /// <returns><c>true</c> if the item was enqueued, otherwise <c>false</c>ß</returns>
    public bool Enqueue(Task<TItem> item) =>
        Enqueue(new BatchItem<TItem, TBatchContext>(_ => item));

    /// <summary>
    /// Enqueues a batch item and returns true if the item was successfully enqueued.
    /// </summary>
    /// <param name="item">The item to enqueue</param>
    /// <returns><c>true</c> if the item was enqueued, otherwise <c>false</c>ß</returns>
    public bool Enqueue(BatchItem<TItem, TBatchContext> item)
    {
        if (Count >= _options.Value.MaxQueueSize)
        {
            _logger.LogWarningMaxQueueSizeReached(_options.Value.MaxQueueSize, Count);
        }

        if (!_channel.Writer.TryWrite(item))
        {
            _logger.LogWarningCannotEnqueueEvent(_disposed is 1);
            return false;
        }

        if (Count < _options.Value.FlushAt)
        {
            return true;
        }

        _logger.LogTraceFlushCalledOnCaptureFlushAt(_options.Value.FlushAt, Count);
        // Signal that a flush is needed.
        SignalFlush();
        return true;
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
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // When running locally we want this to throw so we can see the exception.
            Debug.Assert(
                ex is not ArgumentException and not NullReferenceException,
                $"Unexpected {ex.GetType().FullName} occurred during async batch handling."
            );

            _logger.LogErrorUnexpectedException(ex);
        }
    }

    async Task HandleTimer(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken))
            {
                if (Count <= 0)
                {
                    continue;
                }
                _logger.LogTraceFlushCalledOnFlushInterval(_options.Value.FlushInterval, Count);
                // Signal that a flush is needed.
                SignalFlush();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogTraceOperationCancelled(nameof(HandleTimer));
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // When running locally we want this to throw so we can see the exception.
            Debug.Assert(
                ex is not ArgumentException and not NullReferenceException,
                $"Unexpected {ex.GetType().FullName} occurred during async batch handling."
            );

            _logger.LogErrorUnexpectedException(ex);
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

        try
        {
            var batchContext = _batchContextFunc();
            while (_channel.Reader.TryReadBatch(_options.Value.MaxBatchSize, out var batch))
            {
                var tasks = batch.Select(item => item.ResolveItem(batchContext));
                var resolved = await Task.WhenAll(tasks);
                await SendBatch(resolved);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
        }

        return;

        async Task SendBatch(IReadOnlyCollection<TItem> batch)
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
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception e)
#pragma warning restore CA1031
        {
            // When running locally we want this to throw so we can see the exception.
            Debug.Assert(
                e is not ArgumentException and not NullReferenceException,
                $"Unexpected {e.GetType().FullName} occurred during async batch handling."
            );

            _logger.LogErrorUnexpectedException(e);
        }
    }
}

internal abstract class AsyncBatchHandler;

internal static partial class AsyncBatchHandlerLoggerExtensions
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Debug, Message = "Sending Batch: {Count} items")]
    public static partial void LogDebugSendingBatch(
        this ILogger<AsyncBatchHandler> logger,
        int count
    );

    [LoggerMessage(
        EventId = 101,
        Level = LogLevel.Trace,
        Message = "Batch sent: Queue is now at {Count} items"
    )]
    public static partial void LogTraceBatchSent(this ILogger<AsyncBatchHandler> logger, int count);

    [LoggerMessage(
        EventId = 102,
        Level = LogLevel.Trace,
        Message = "Flush called on capture because FlushAt ({FlushAt}) count met, {Count} items in the queue"
    )]
    public static partial void LogTraceFlushCalledOnCaptureFlushAt(
        this ILogger<AsyncBatchHandler> logger,
        int flushAt,
        int count
    );

    [LoggerMessage(
        EventId = 103,
        Level = LogLevel.Trace,
        Message = "Flush called on the Flush Interval: {Interval}, {Count} items in the queue"
    )]
    public static partial void LogTraceFlushCalledOnFlushInterval(
        this ILogger<AsyncBatchHandler> logger,
        TimeSpan interval,
        int count
    );

    [LoggerMessage(
        EventId = 104,
        Level = LogLevel.Trace,
        Message = "Flush called because we're disposing: {Count} items in the queue"
    )]
    public static partial void LogTraceFlushCalledInDispose(
        this ILogger<AsyncBatchHandler> logger,
        int count
    );

    [LoggerMessage(
        EventId = 105,
        Level = LogLevel.Information,
        Message = "Flush called directly via code: {Count} items in the queue"
    )]
    public static partial void LogInfoFlushCalledDirectly(
        this ILogger<AsyncBatchHandler> logger,
        int count
    );

    [LoggerMessage(
        EventId = 106,
        Level = LogLevel.Information,
        Message = "DisposeAsync called in AsyncBatchHandler"
    )]
    public static partial void LogInfoDisposeAsyncCalled(this ILogger<AsyncBatchHandler> logger);

    [LoggerMessage(
        EventId = 107,
        Level = LogLevel.Warning,
        Message = "Cannot enqueue event. Disposed: {Disposed}"
    )]
    public static partial void LogWarningCannotEnqueueEvent(
        this ILogger<AsyncBatchHandler> logger,
        bool disposed
    );

    [LoggerMessage(
        EventId = 108,
        Level = LogLevel.Warning,
        Message = "Dispose called a second time. Ignoring"
    )]
    public static partial void LogWarningDisposeCalledTwice(this ILogger<AsyncBatchHandler> logger);

    [LoggerMessage(
        EventId = 110,
        Level = LogLevel.Trace,
        Message = "{MethodName} exiting due to OperationCancelled exception"
    )]
    public static partial void LogTraceOperationCancelled(
        this ILogger<AsyncBatchHandler> logger,
        string methodName
    );

    [LoggerMessage(
        EventId = 111,
        Level = LogLevel.Warning,
        Message = "MaxQueueSize ({MaxQueueSize}) reached. Count: {count}. Dropping oldest item."
    )]
    public static partial void LogWarningMaxQueueSizeReached(
        this ILogger<AsyncBatchHandler> logger,
        int maxQueueSize,
        int count
    );

    [LoggerMessage(
        EventId = 500,
        Level = LogLevel.Error,
        Message = "Unexpected exception occurred during async batch handling."
    )]
    public static partial void LogErrorUnexpectedException(
        this ILogger<AsyncBatchHandler> logger,
        Exception exception
    );
}
