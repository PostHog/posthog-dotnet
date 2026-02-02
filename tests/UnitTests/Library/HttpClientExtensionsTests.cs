using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PostHog;
using PostHog.Api;
using PostHog.Library;
#if NETCOREAPP3_1
using TestLibrary.Fakes.Polyfills;
#endif

namespace HttpClientExtensionsTests;

public class ThePostJsonWithRetryAsyncMethod
{
    static readonly Uri BatchUrl = new("https://us.i.posthog.com/batch");

    static PostHogOptions CreateOptions(
        int maxRetries = 3,
        TimeSpan? initialRetryDelay = null,
        TimeSpan? maxRetryDelay = null,
        bool enableCompression = false) => new()
        {
            ProjectApiKey = "test-api-key",
            MaxRetries = maxRetries,
            InitialRetryDelay = initialRetryDelay ?? TimeSpan.FromMilliseconds(1),
            MaxRetryDelay = maxRetryDelay ?? TimeSpan.FromSeconds(30),
            EnableCompression = enableCompression
        };

    static HttpClient CreateHttpClient(FakeRetryHttpMessageHandler handler)
        => new(handler) { BaseAddress = new Uri("https://us.i.posthog.com") };

    [Fact]
    public async Task ReturnsSuccessOnFirstAttemptWithNoRetry()
    {
        var handler = new FakeRetryHttpMessageHandler();
        handler.AddResponse(HttpStatusCode.OK, new { status = 1 });
        using var httpClient = CreateHttpClient(handler);
        var options = CreateOptions();
        var timeProvider = new FakeTimeProvider();

        var result = await httpClient.PostJsonWithRetryAsync<ApiResult>(
            BatchUrl,
            new { api_key = "test", batch = Array.Empty<object>() },
            timeProvider,
            options,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.Status);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)] // 500
    [InlineData(HttpStatusCode.BadGateway)] // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)] // 503
    [InlineData(HttpStatusCode.GatewayTimeout)] // 504
    [InlineData(HttpStatusCode.RequestTimeout)] // 408
    [InlineData(HttpStatusCode.TooManyRequests)] // 429
    public async Task RetriesOnRetryableStatusCodeThenSucceeds(HttpStatusCode statusCode)
    {
        var handler = new FakeRetryHttpMessageHandler();
        handler.AddResponse(statusCode, new { error = "transient" });
        handler.AddResponse(HttpStatusCode.OK, new { status = 1 });
        using var httpClient = CreateHttpClient(handler);
        var options = CreateOptions(maxRetries: 3);
        var timeProvider = new FakeTimeProvider();

        // Start the request
        var task = httpClient.PostJsonWithRetryAsync<ApiResult>(
            BatchUrl,
            new { api_key = "test", batch = Array.Empty<object>() },
            timeProvider,
            options,
            CancellationToken.None);

        // Let the first request complete and start waiting
        await Task.Delay(10);

        // Advance time to trigger the retry
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await task;

        Assert.NotNull(result);
        Assert.Equal(1, result.Status);
        Assert.Equal(2, handler.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)] // 400
    [InlineData(HttpStatusCode.Unauthorized)] // 401
    [InlineData(HttpStatusCode.Forbidden)] // 403
    [InlineData(HttpStatusCode.NotFound)] // 404
    public async Task DoesNotRetryOnNonRetryableStatusCode(HttpStatusCode statusCode)
    {
        var handler = new FakeRetryHttpMessageHandler();
        handler.AddResponse(statusCode, new { type = "error", detail = "Bad request" });
        handler.AddResponse(HttpStatusCode.OK, new { status = 1 }); // Should never be reached
        using var httpClient = CreateHttpClient(handler);
        var options = CreateOptions(maxRetries: 3);
        var timeProvider = new FakeTimeProvider();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            httpClient.PostJsonWithRetryAsync<ApiResult>(
                BatchUrl,
                new { api_key = "test", batch = Array.Empty<object>() },
                timeProvider,
                options,
                CancellationToken.None));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ThrowsAfterMaxRetriesWhenAllAttemptsFail()
    {
        var handler = new FakeRetryHttpMessageHandler();
        // Add 4 failures (1 initial + 3 retries)
        handler.AddResponse(HttpStatusCode.ServiceUnavailable, new { type = "error", detail = "Down" });
        handler.AddResponse(HttpStatusCode.ServiceUnavailable, new { type = "error", detail = "Down" });
        handler.AddResponse(HttpStatusCode.ServiceUnavailable, new { type = "error", detail = "Down" });
        handler.AddResponse(HttpStatusCode.ServiceUnavailable, new { type = "error", detail = "Down" });
        using var httpClient = CreateHttpClient(handler);
        var options = CreateOptions(maxRetries: 3);
        var timeProvider = new FakeTimeProvider();

        var task = httpClient.PostJsonWithRetryAsync<ApiResult>(
            BatchUrl,
            new { api_key = "test", batch = Array.Empty<object>() },
            timeProvider,
            options,
            CancellationToken.None);

        // Keep advancing time until the task completes or we time out
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(10);
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            if (task.IsCompleted)
            {
                break;
            }
        }

        await Assert.ThrowsAsync<ApiException>(() => task);
        Assert.Equal(4, handler.RequestCount); // 1 initial + 3 retries
    }

    [Fact]
    public async Task RespectsRetryAfterDeltaHeader()
    {
        var handler = new FakeRetryHttpMessageHandler();
        using var responseWithRetryAfter = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"type\": \"error\", \"detail\": \"rate limited\"}")
        };
        responseWithRetryAfter.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(100));
        handler.AddResponse(responseWithRetryAfter);
        handler.AddResponse(HttpStatusCode.OK, new { status = 1 });
        using var httpClient = CreateHttpClient(handler);
        var options = CreateOptions(maxRetries: 3);
        var timeProvider = new FakeTimeProvider();

        var task = httpClient.PostJsonWithRetryAsync<ApiResult>(
            BatchUrl,
            new { api_key = "test", batch = Array.Empty<object>() },
            timeProvider,
            options,
            CancellationToken.None);

        // Let the first request complete and start waiting for retry
        await Task.Delay(10);

#if NET8_0_OR_GREATER
        // Verify task is waiting for the Retry-After delay
        Assert.False(task.IsCompleted, "Task should be waiting for Retry-After delay");
#endif

        // Advance time by the Retry-After value
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        var result = await task;

        Assert.NotNull(result);
        Assert.Equal(1, result.Status);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task RespectsRetryAfterDateHeader()
    {
        var handler = new FakeRetryHttpMessageHandler();
        using var responseWithRetryAfter = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"type\": \"error\", \"detail\": \"rate limited\"}")
        };
        var timeProvider = new FakeTimeProvider();
        // Set Retry-After to a date 100ms in the future
        var retryAfterDate = timeProvider.GetUtcNow().AddMilliseconds(100);
        responseWithRetryAfter.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfterDate);
        handler.AddResponse(responseWithRetryAfter);
        handler.AddResponse(HttpStatusCode.OK, new { status = 1 });
        using var httpClient = CreateHttpClient(handler);
        var options = CreateOptions(maxRetries: 3);

        var task = httpClient.PostJsonWithRetryAsync<ApiResult>(
            BatchUrl,
            new { api_key = "test", batch = Array.Empty<object>() },
            timeProvider,
            options,
            CancellationToken.None);

        // Let the first request complete and start waiting for retry
        await Task.Delay(10);

#if NET8_0_OR_GREATER
        // Verify task is waiting for the Retry-After delay
        Assert.False(task.IsCompleted, "Task should be waiting for Retry-After date");
#endif

        // Advance time past the Retry-After date
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        var result = await task;

        Assert.NotNull(result);
        Assert.Equal(1, result.Status);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task CapsRetryDelayAtMaxRetryDelay()
    {
        var handler = new FakeRetryHttpMessageHandler();
        using var responseWithLargeRetryAfter = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"type\": \"error\", \"detail\": \"rate limited\"}")
        };
        // Server requests 500ms delay, but our max is 50ms
        responseWithLargeRetryAfter.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(500));
        handler.AddResponse(responseWithLargeRetryAfter);
        handler.AddResponse(HttpStatusCode.OK, new { status = 1 });
        using var httpClient = CreateHttpClient(handler);
        var options = CreateOptions(maxRetries: 3, maxRetryDelay: TimeSpan.FromMilliseconds(50));
        var timeProvider = new FakeTimeProvider();

        var task = httpClient.PostJsonWithRetryAsync<ApiResult>(
            BatchUrl,
            new { api_key = "test", batch = Array.Empty<object>() },
            timeProvider,
            options,
            CancellationToken.None);

        // Let the first request complete
        await Task.Delay(10);

#if NET8_0_OR_GREATER
        // Verify task is waiting (delay was capped, not skipped)
        Assert.False(task.IsCompleted, "Task should be waiting for capped delay");
#endif

        // Advance time by max delay (50ms), not the full 500ms - should be enough due to capping
        timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        var result = await task;

        Assert.NotNull(result);
        Assert.Equal(1, result.Status);
    }

    [Fact]
    public async Task RetriesOnHttpRequestException()
    {
        var handler = new FakeRetryHttpMessageHandler();
        handler.AddException(new HttpRequestException("Network error"));
        handler.AddResponse(HttpStatusCode.OK, new { status = 1 });
        using var httpClient = CreateHttpClient(handler);
        var options = CreateOptions(maxRetries: 3);
        var timeProvider = new FakeTimeProvider();

        var task = httpClient.PostJsonWithRetryAsync<ApiResult>(
            BatchUrl,
            new { api_key = "test", batch = Array.Empty<object>() },
            timeProvider,
            options,
            CancellationToken.None);

        await Task.Delay(10);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await task;

        Assert.NotNull(result);
        Assert.Equal(1, result.Status);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task RetriesUntilSuccessAfterMultipleServiceUnavailableResponses()
    {
        var handler = new FakeRetryHttpMessageHandler();
        handler.AddResponse(HttpStatusCode.ServiceUnavailable, new { type = "error" });
        handler.AddResponse(HttpStatusCode.ServiceUnavailable, new { type = "error" });
        handler.AddResponse(HttpStatusCode.ServiceUnavailable, new { type = "error" });
        handler.AddResponse(HttpStatusCode.OK, new { status = 1 });
        using var httpClient = CreateHttpClient(handler);
        // Use small delays for fast tests
        var options = CreateOptions(maxRetries: 3, initialRetryDelay: TimeSpan.FromMilliseconds(10));
        var timeProvider = new FakeTimeProvider();

        var task = httpClient.PostJsonWithRetryAsync<ApiResult>(
            BatchUrl,
            new { api_key = "test", batch = Array.Empty<object>() },
            timeProvider,
            options,
            CancellationToken.None);

        // Advance time incrementally to trigger each retry with exponential backoff
        // Delays: 10ms, 20ms, 40ms (doubled each time)
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(10);
            timeProvider.Advance(TimeSpan.FromMilliseconds(50));
            if (task.IsCompleted)
            {
                break;
            }
        }

        var result = await task;

        Assert.NotNull(result);
        Assert.Equal(1, result.Status);
        Assert.Equal(4, handler.RequestCount); // 1 initial + 3 retries
    }

    [Fact]
    public async Task MaxRetriesZeroMeansNoRetry()
    {
        var handler = new FakeRetryHttpMessageHandler();
        handler.AddResponse(HttpStatusCode.ServiceUnavailable, new { type = "error", detail = "Down" });
        handler.AddResponse(HttpStatusCode.OK, new { status = 1 }); // Should never be reached
        using var httpClient = CreateHttpClient(handler);
        var options = CreateOptions(maxRetries: 0);
        var timeProvider = new FakeTimeProvider();

        await Assert.ThrowsAsync<ApiException>(() =>
            httpClient.PostJsonWithRetryAsync<ApiResult>(
                BatchUrl,
                new { api_key = "test", batch = Array.Empty<object>() },
                timeProvider,
                options,
                CancellationToken.None));

        Assert.Equal(1, handler.RequestCount); // Only the initial attempt
    }
}

public class ThePostCompressedJsonAsyncMethod
{
    static readonly Uri BatchUrl = new("https://us.i.posthog.com/batch");

    [Fact]
    public async Task CompressesRequestBodyWithGzip()
    {
        byte[]? capturedBody = null;
        IEnumerable<string>? capturedContentEncoding = null;
        string? capturedContentType = null;

        var handler = new LambdaHttpMessageHandler(async request =>
        {
            capturedContentType = request.Content?.Headers.ContentType?.MediaType;
            capturedContentEncoding = request.Content?.Headers.ContentEncoding;
            if (request.Content != null)
            {
                capturedBody = await request.Content.ReadAsByteArrayAsync();
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\": 1}")
            };
        });

        using var httpClient = new HttpClient(handler);
        var options = new PostHogOptions
        {
            ProjectApiKey = "test-api-key",
            EnableCompression = true
        };
        var timeProvider = new FakeTimeProvider();
        var payload = new { api_key = "test", batch = new[] { new { @event = "test-event" } } };

        var result = await httpClient.PostJsonWithRetryAsync<ApiResult>(
            BatchUrl,
            payload,
            timeProvider,
            options,
            CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Equal("application/json", capturedContentType);
        Assert.Contains("gzip", capturedContentEncoding!);

        // Decompress and verify content
        using var compressedStream = new MemoryStream(capturedBody);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzipStream, Encoding.UTF8);
        var decompressedJson = await reader.ReadToEndAsync();

        Assert.Contains("test-event", decompressedJson, StringComparison.Ordinal);
        Assert.Contains("api_key", decompressedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotCompressWhenCompressionDisabled()
    {
        IEnumerable<string>? capturedContentEncoding = null;

        var handler = new LambdaHttpMessageHandler(request =>
        {
            capturedContentEncoding = request.Content?.Headers.ContentEncoding;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\": 1}")
            });
        });

        using var httpClient = new HttpClient(handler);
        var options = new PostHogOptions
        {
            ProjectApiKey = "test-api-key",
            EnableCompression = false
        };
        var timeProvider = new FakeTimeProvider();
        var payload = new { api_key = "test", batch = new[] { new { @event = "test-event" } } };

        await httpClient.PostJsonWithRetryAsync<ApiResult>(
            BatchUrl,
            payload,
            timeProvider,
            options,
            CancellationToken.None);

        Assert.Empty(capturedContentEncoding ?? Enumerable.Empty<string>());
    }
}

/// <summary>
/// A fake HTTP message handler for testing retry logic.
/// Queues responses that are returned in order.
/// </summary>
sealed class FakeRetryHttpMessageHandler : HttpMessageHandler
{
    readonly Queue<Func<Task<HttpResponseMessage>>> _responses = new();
    int _requestCount;

    public int RequestCount => _requestCount;

    public void AddResponse(HttpStatusCode statusCode, object body)
    {
        var json = JsonSerializer.Serialize(body);
        _responses.Enqueue(() => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
    }

    public void AddResponse(HttpResponseMessage response)
    {
        _responses.Enqueue(() => Task.FromResult(response));
    }

    public void AddException(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _requestCount++;

        if (_responses.Count == 0)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        return await _responses.Dequeue()();
    }
}

/// <summary>
/// A simple lambda-based HTTP message handler for testing.
/// </summary>
sealed class LambdaHttpMessageHandler(
    Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => handler(request);
}
