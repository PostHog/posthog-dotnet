
using Microsoft.Extensions.Time.Testing;
using PostHog;
using PostHog.Library;
#if NETCOREAPP3_1
using TestLibrary.Fakes.Polyfills;
#endif
namespace AsyncBatchHandlerTests;

public class TheEnqueueMethod
{
    [Fact]
    public async Task CallsBatchHandlerWhenThresholdMet()
    {
        var options = new FakeOptions<PostHogOptions>(new()
        {
            FlushAt = 3,
            FlushInterval = TimeSpan.FromHours(3)
        });
        var items = new List<int>();
        var handlerCompleteTask = new TaskCompletionSource();
        Func<IEnumerable<int>, Task> handlerFunc = batch =>
        {
            items.AddRange(batch);
            handlerCompleteTask.SetResult();
            return Task.CompletedTask;
        };
        await using var batchHandler = new AsyncBatchHandler<int>(handlerFunc, new FakeTimeProvider(), options);

        batchHandler.Enqueue(Task.FromResult(1));
        Assert.Empty(items);
        batchHandler.Enqueue(Task.FromResult(2));
        Assert.Empty(items);
        batchHandler.Enqueue(Task.FromResult(3));
        await handlerCompleteTask.Task;
        Assert.Equal([1, 2, 3], items);
    }

    [Fact]
    public async Task CallsBatchHandlerUntilQueueDrained()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new FakeOptions<PostHogOptions>(new()
        {
            FlushAt = 1000,
            FlushInterval = TimeSpan.FromSeconds(10),
            MaxBatchSize = 3
        });
        var items = new List<int>();
        int callCount = 0;
        Func<IEnumerable<int>, Task> handlerFunc = batch =>
        {
            items.AddRange(batch);
            callCount++;
            return Task.CompletedTask;
        };
        await using var batchHandler = new AsyncBatchHandler<int>(handlerFunc, timeProvider, options);

        batchHandler.Enqueue(Task.FromResult(1));
        batchHandler.Enqueue(Task.FromResult(2));
        batchHandler.Enqueue(Task.FromResult(3));
        batchHandler.Enqueue(Task.FromResult(4));
        batchHandler.Enqueue(Task.FromResult(5));
        batchHandler.Enqueue(Task.FromResult(6));
        batchHandler.Enqueue(Task.FromResult(7));
        Assert.Empty(items);

        await batchHandler.FlushAsync();
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], items);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task FlushBatchAsyncContinuesAfterException()
    {
        var options = new FakeOptions<PostHogOptions>(new()
        {
            MaxBatchSize = 2,
            FlushInterval = TimeSpan.FromHours(3)
        });
        var items = new List<int>();
        int callCount = 0;
        Func<IEnumerable<int>, Task> handlerFunc = batch =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new InvalidOperationException("Test exception");
            }
            items.AddRange(batch);
            return Task.CompletedTask;
        };
        await using var batchHandler = new AsyncBatchHandler<int>(handlerFunc, new FakeTimeProvider(), options);

        batchHandler.Enqueue(Task.FromResult(1));
        batchHandler.Enqueue(Task.FromResult(2));
        batchHandler.Enqueue(Task.FromResult(3));

        // First flush attempt should throw an exception
        await Assert.ThrowsAsync<InvalidOperationException>(() => batchHandler.FlushAsync());

        // Second flush attempt should succeed
        await batchHandler.FlushAsync();

        // Verify that the items were flushed successfully on the second attempt
        Assert.Equal([3], items);
    }

    [Fact]
    public async Task DropsOlderEventsWhenMaxQueueMet()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new FakeOptions<PostHogOptions>(new()
        {
            FlushAt = 100,
            MaxQueueSize = 5, // In practice, this should be smaller than FlushAt. But for this test, this is needed.
            FlushInterval = TimeSpan.FromHours(3)
        });
        var items = new List<int>();
        Func<IEnumerable<int>, Task> handlerFunc = batch =>
        {
            items.AddRange(batch);
            return Task.CompletedTask;
        };
        await using var batchHandler = new AsyncBatchHandler<int>(handlerFunc, timeProvider, options);

        // Burst of events before flush can run.
        for (var i = 1; i <= 10; i++)
        {
            batchHandler.Enqueue(Task.FromResult(i));
        }

        Assert.Equal(5, batchHandler.Count);

        // Ensure no items are flushed yet.
        Assert.Empty(items);

        await batchHandler.FlushAsync();
        Assert.Equal([6, 7, 8, 9, 10], items);
    }

    [Fact]
    public async Task FlushesBatchOnTimer()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new FakeOptions<PostHogOptions>(new()
        {
            FlushAt = 10,
            FlushInterval = TimeSpan.FromSeconds(2)
        });
        var items = new List<int>();
        var handlerCompleteTask = new TaskCompletionSource();
        Func<IEnumerable<int>, Task> handlerFunc = batch =>
        {
            items.AddRange(batch);
            handlerCompleteTask.SetResult();
            return Task.CompletedTask;
        };

        await using var batchHandler = new AsyncBatchHandler<int>(handlerFunc, timeProvider, options);
        batchHandler.Enqueue(Task.FromResult(1));
        Assert.Empty(items);
        batchHandler.Enqueue(Task.FromResult(2));
        Assert.Empty(items);
        batchHandler.Enqueue(Task.FromResult(3));
        Assert.Empty(items);

        // Simulate the passage of time.
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        // Ensure empty because we only advanced 1 second, but the interval is 2 seconds.
        Assert.Empty(items);
        timeProvider.Advance(TimeSpan.FromSeconds(1.1));
        // Ok, we should be flushing. Let's wait for that to complete.
        var timeout = TimeSpan.FromSeconds(1); // Increase timeout to ensure completion
        var completedTask = await Task.WhenAny(handlerCompleteTask.Task, Task.Delay(timeout));

        if (completedTask == handlerCompleteTask.Task)
        {
            // The handler completed within the timeout
            await handlerCompleteTask.Task; // Ensure any exceptions/cancellation are observed
        }
        else
        {
            // The timeout occurred
            throw new TimeoutException("The operation timed out.");
        }

        // The batch should be done flushing due to the timer interval.
        Assert.Equal(new[] { 1, 2, 3 }, items);
    }

    [Fact]
    public async Task IgnoresEnqueuedItemAfterDispose()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new FakeOptions<PostHogOptions>(new()
        {
            FlushAt = 2,
            FlushInterval = TimeSpan.FromSeconds(2)
        });
        var items = new List<int>();
        var handlerCompleteTask = new TaskCompletionSource();
        Func<IEnumerable<int>, Task> handlerFunc = batch =>
        {
            items.AddRange(batch);
            handlerCompleteTask.SetResult();
            return Task.CompletedTask;
        };

        var batchHandler = new AsyncBatchHandler<int>(handlerFunc, timeProvider, options);
        batchHandler.Enqueue(Task.FromResult(1));

        await batchHandler.DisposeAsync();

        Assert.Equal([1], items);

        batchHandler.Enqueue(Task.FromResult(2));
        batchHandler.Enqueue(Task.FromResult(3));

        Assert.Equal(0, batchHandler.Count);
    }
}

public class TheDisposeAsyncMethod
{
    [Fact]
    public async Task FlushesBatchWhenDisposed()
    {
        var timeProvider = new FakeTimeProvider();
        var handlerCompleteTask = new TaskCompletionSource();
        var options = new FakeOptions<PostHogOptions>(new()
        {
            FlushAt = 3,
            FlushInterval = TimeSpan.FromHours(3)
        });
        var items = new List<int>();
        Func<IEnumerable<int>, Task> handlerFunc = batch =>
        {
            items.AddRange(batch);
            handlerCompleteTask.SetResult();
            return Task.CompletedTask;
        };

        await using (var batchHandler = new AsyncBatchHandler<int>(handlerFunc, timeProvider, options))
        {
            batchHandler.Enqueue(Task.FromResult(1));
            Assert.Empty(items);
            batchHandler.Enqueue(Task.FromResult(2));
            Assert.Empty(items);
        }

        var timeout = TimeSpan.FromSeconds(1);
        var completedTask = await Task.WhenAny(handlerCompleteTask.Task, Task.Delay(timeout)); // Wait for the flush invoked by DisposeAsync to complete.

        Assert.Equal([1, 2], items);
    }

    [Fact]
    public async Task DoesNotDisposeTwice()
    {
        var options = new FakeOptions<PostHogOptions>(new()
        {
            FlushAt = 3,
            FlushInterval = TimeSpan.FromHours(3)
        });
        var items = new List<int>();
        var handlerCompleteTask = new TaskCompletionSource();
        Func<IEnumerable<int>, Task> handlerFunc = batch =>
        {
            items.AddRange(batch);
            if (!handlerCompleteTask.Task.IsCompleted)
            {
                handlerCompleteTask.SetResult();
            }

            return Task.CompletedTask;
        };

        var batchHandler = new AsyncBatchHandler<int>(handlerFunc, new FakeTimeProvider(), options);

        batchHandler.Enqueue(Task.FromResult(1));
        Assert.Empty(items);
        batchHandler.Enqueue(Task.FromResult(2));
        Assert.Empty(items);

        await Task.WhenAll(batchHandler.DisposeAsync().AsTask(), batchHandler.DisposeAsync().AsTask());
        await handlerCompleteTask.Task;

        Assert.Equal([1, 2], items);
    }

    [Fact]
    public async Task HandlesExceptionsInFlushBatchAsync()
    {
        var options = new FakeOptions<PostHogOptions>(new()
        {
            FlushAt = 9,
            FlushInterval = TimeSpan.FromHours(3)
        });
        Func<IEnumerable<int>, Task> handlerFunc = batch => throw new HttpRequestException("Test exception");

        await using var batchHandler = new AsyncBatchHandler<int>(handlerFunc, new FakeTimeProvider(), options);
        batchHandler.Enqueue(Task.FromResult(1));
        batchHandler.Enqueue(Task.FromResult(2));

        // Test succeeds if no exception is thrown.
    }
}