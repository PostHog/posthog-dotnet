using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PostHog.Api;
using PostHog.Json;

namespace PostHog.Library;

/// <summary>
/// Extension methods for <see cref="HttpClient"/>.
/// </summary>
internal static class HttpClientExtensions
{
    /// <summary>
    /// Sends a POST request to the specified Uri containing the value serialized as JSON in the request body.
    /// Returns the response body deserialized as <typeparamref name="TBody"/>.
    /// </summary>
    /// <param name="httpClient">The client used to send the request.</param>
    /// <param name="requestUri">The Uri the request is sent to.</param>
    /// <param name="content">The value to serialize.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <typeparam name="TBody">The type of the response body to deserialize to.</typeparam>
    /// <returns>The task representing the asynchronous operation.</returns>
    public static async Task<TBody?> PostJsonAsync<TBody>(
        this HttpClient httpClient,
        Uri requestUri,
        object content,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            requestUri,
            content,
            JsonSerializerHelper.Options,
            cancellationToken);

        await response.EnsureSuccessfulApiCall(cancellationToken);

        var result = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializerHelper.DeserializeFromCamelCaseJsonAsync<TBody>(
            result,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends a POST request with retry logic for transient failures.
    /// Retries on 5xx, 408 (Request Timeout), and 429 (Too Many Requests) status codes.
    /// Optionally compresses the request body with gzip.
    /// </summary>
    public static async Task<TBody?> PostJsonWithRetryAsync<TBody>(
        this HttpClient httpClient,
        Uri requestUri,
        object content,
        TimeProvider timeProvider,
        PostHogOptions options,
        CancellationToken cancellationToken)
    {
        var maxRetries = options.MaxRetries;
        var currentDelay = options.InitialRetryDelay;
        var maxDelay = options.MaxRetryDelay;
        var enableCompression = options.EnableCompression;
        var attempt = 0;

        while (true)
        {
            attempt++;
            HttpResponseMessage? response = null;
            TimeSpan? retryDelay = null;
            Exception? exceptionToThrow = null;

            try
            {
                response = enableCompression
                    ? await PostCompressedJsonAsync(httpClient, requestUri, content, cancellationToken)
                    : await httpClient.PostAsJsonAsync(
                        requestUri,
                        content,
                        JsonSerializerHelper.Options,
                        cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    using var successResponse = response;
                    response = null; // Prevent double-disposal in outer finally
                    var result = await successResponse.Content.ReadAsStreamAsync(cancellationToken);
                    return await JsonSerializerHelper.DeserializeFromCamelCaseJsonAsync<TBody>(
                        result,
                        cancellationToken: cancellationToken);
                }

                // Check if we should retry
                if (!ShouldRetry(response.StatusCode) || attempt > maxRetries)
                {
                    // Capture exception to throw after disposal - throwing inside try would be caught by retry logic
                    exceptionToThrow = await CreateApiException(response, cancellationToken);
                }
                else
                {
                    // Signal retry with Retry-After header support
                    retryDelay = GetRetryDelay(response, currentDelay, maxDelay, timeProvider);
                }
            }
            catch (HttpRequestException) when (attempt <= maxRetries)
            {
                // Network errors are retryable with default delay, capped at maxDelay
                retryDelay = currentDelay > maxDelay ? maxDelay : currentDelay;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt <= maxRetries)
            {
                // HttpClient timeout (not user cancellation) - retry with backoff
                retryDelay = currentDelay > maxDelay ? maxDelay : currentDelay;
            }
            finally
            {
                response?.Dispose();
            }

            // Throw outside try-catch so non-retryable errors won't be caught by retry logic
            if (exceptionToThrow != null)
            {
                throw exceptionToThrow;
            }

            // Retry delay and exponential backoff (single location for both code paths)
            if (retryDelay.HasValue)
            {
                await Delay(timeProvider, retryDelay.Value, cancellationToken);
                currentDelay = DoubleWithCap(currentDelay, maxDelay);
            }
            else
            {
                // Defensive guard: loop should always either retry or throw. We've already thrown
                // above if exceptionToThrow was set, so reaching here without retryDelay indicates
                // a logic error that could cause an infinite loop.
                throw new InvalidOperationException(
                    "Retry loop invariant broken: neither retry nor exception was set.");
            }
        }
    }

    /// <summary>
    /// Determines if a status code indicates a transient failure that should be retried.
    /// </summary>
    /// <remarks>
    /// Note: 429 (Too Many Requests) is retried here but not in the Python SDK. This is acceptable
    /// because the .NET SDK only applies retry logic to the batch endpoint, which is idempotent due
    /// to UUID-based event deduplication. Additionally, Retry-After headers are respected and capped
    /// at MaxRetryDelay, preventing server-controlled indefinite delays.
    /// </remarks>
    static bool ShouldRetry(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is 408 // Request Timeout
            or 429 // Too Many Requests
            or (>= 500 and <= 599); // 5xx
    }

    /// <summary>
    /// Doubles the delay with overflow protection, capping at maxDelay.
    /// </summary>
    internal static TimeSpan DoubleWithCap(TimeSpan current, TimeSpan max)
    {
        var currentTicks = current.Ticks;
        var maxTicks = max.Ticks;

        // If already at or above max, stay at max
        if (currentTicks >= maxTicks)
        {
            return max;
        }

        // If doubling would overflow or exceed max, cap at max
        if (currentTicks > maxTicks / 2)
        {
            return max;
        }

        return TimeSpan.FromTicks(currentTicks * 2);
    }

    static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        TimeSpan defaultDelay,
        TimeSpan maxDelay,
        TimeProvider timeProvider)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return defaultDelay;
        }

        TimeSpan delay;
        if (retryAfter.Delta.HasValue)
        {
            delay = retryAfter.Delta.Value;
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }
        }
        else if (retryAfter.Date.HasValue)
        {
            delay = retryAfter.Date.Value - timeProvider.GetUtcNow();
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }
        }
        else
        {
            return defaultDelay;
        }

        // Cap at maxDelay
        return delay > maxDelay ? maxDelay : delay;
    }

    /// <summary>
    /// Creates an exception for a failed API response without throwing it.
    /// This allows the exception to be thrown outside the try-catch block so non-retryable errors
    /// won't be caught by retry logic.
    /// </summary>
    static async Task<Exception> CreateApiException(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Use EnsureSuccessStatusCode to get HttpRequestException with proper metadata
            // (including StatusCode property on .NET 5+)
            try
            {
                response.EnsureSuccessStatusCode();
                // Should never reach here since status is 404
                return new HttpRequestException(
                    $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }
            catch (HttpRequestException ex)
            {
                return ex;
            }
        }

        var (error, deserializationException) = await TryReadApiErrorResultAsync(response, cancellationToken);

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new UnauthorizedAccessException(
                error?.Detail ?? "Unauthorized. Could not deserialize the response for more info.",
                deserializationException),
            _ => new ApiException(error, response.StatusCode, deserializationException)
        };
    }

    /// <summary>
    /// Attempts to deserialize an API error response. Returns the error result and any
    /// exception that occurred during deserialization.
    /// </summary>
    static async Task<(ApiErrorResult?, Exception?)> TryReadApiErrorResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            // CA2016: The ReadFromJsonAsync overload that accepts both JsonSerializerOptions and
            // CancellationToken is only available on .NET 5+. On netstandard2.0/netstandard2.1,
            // only the overload with cancellationToken (no options) is available. The cancellation
            // token is still passed and will be respected. This suppression can be removed when
            // these older targets are dropped from the SDK.
#pragma warning disable CA2016
            var result = await response.Content.ReadFromJsonAsync<ApiErrorResult>(
                cancellationToken: cancellationToken);
#pragma warning restore CA2016
            return (result, null);
        }
        catch (JsonException e)
        {
            return (null, e);
        }
    }

    static Task Delay(TimeProvider timeProvider, TimeSpan delay, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        return Task.Delay(delay, timeProvider, cancellationToken);
#else
        return Task.Delay(delay, cancellationToken);
#endif
    }

    static async Task<HttpResponseMessage> PostCompressedJsonAsync(
        HttpClient httpClient,
        Uri requestUri,
        object content,
        CancellationToken cancellationToken)
    {
        // Stream JSON directly into gzip to avoid intermediate allocation
        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Fastest, leaveOpen: true))
        {
            await JsonSerializer.SerializeAsync(gzipStream, content, JsonSerializerHelper.Options, cancellationToken);
        }

        // Use TryGetBuffer to avoid ToArray() copy when possible
        var compressedContent = memoryStream.TryGetBuffer(out var buffer)
            ? new ByteArrayContent(buffer.Array!, buffer.Offset, buffer.Count)
            : new ByteArrayContent(memoryStream.ToArray());

        using (compressedContent)
        {
            compressedContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            compressedContent.Headers.ContentEncoding.Add("gzip");

            return await httpClient.PostAsync(requestUri, compressedContent, cancellationToken);
        }
    }

    public static async Task EnsureSuccessfulApiCall(
        this HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Allow 404 exception to propagate up.
            response.EnsureSuccessStatusCode();
        }

        var (error, exception) = await TryReadApiErrorResultAsync(response, cancellationToken);

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new UnauthorizedAccessException(
                error?.Detail ?? "Unauthorized. Could not deserialize the response for more info.", exception),
            _ => new ApiException(error, response.StatusCode, exception)
        };
    }
}