using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
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
                    var result = await response.Content.ReadAsStreamAsync(cancellationToken);
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
                // Network errors are retryable with default delay
                retryDelay = currentDelay;
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
                currentDelay = TimeSpan.FromTicks(Math.Min(currentDelay.Ticks * 2, maxDelay.Ticks));
            }
        }
    }

    static bool ShouldRetry(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout // 408
            or HttpStatusCode.TooManyRequests // 429
            or >= HttpStatusCode.InternalServerError; // 5xx

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
            // Return HttpRequestException for 404, matching EnsureSuccessStatusCode behavior
            return new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        var (error, deserializationException) = await ReadApiErrorResultAsync();

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new UnauthorizedAccessException(
                error?.Detail ?? "Unauthorized. Could not deserialize the response for more info.",
                deserializationException),
            _ => new ApiException(error, response.StatusCode, deserializationException)
        };

        async Task<(ApiErrorResult?, Exception?)> ReadApiErrorResultAsync()
        {
            try
            {
#pragma warning disable CA2016
                var result = await response.Content.ReadFromJsonAsync<ApiErrorResult>(
                    cancellationToken: cancellationToken);
                return (result, null);
            }
            catch (JsonException e)
            {
                return (null, e);
            }
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
        var json = JsonSerializer.Serialize(content, JsonSerializerHelper.Options);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        byte[] compressedBytes;
        using (var memoryStream = new MemoryStream())
        {
            using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Fastest, leaveOpen: true))
            {
#if NET8_0_OR_GREATER
                await gzipStream.WriteAsync(jsonBytes.AsMemory(), cancellationToken);
#else
                await gzipStream.WriteAsync(jsonBytes, 0, jsonBytes.Length, cancellationToken);
#endif
            }
            compressedBytes = memoryStream.ToArray();
        }

        using var compressedContent = new ByteArrayContent(compressedBytes);
        compressedContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        compressedContent.Headers.ContentEncoding.Add("gzip");

        return await httpClient.PostAsync(requestUri, compressedContent, cancellationToken);
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

        var (error, exception) = await ReadApiErrorResultAsync();

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new UnauthorizedAccessException(
                error?.Detail ?? "Unauthorized. Could not deserialize the response for more info.", exception),
            _ => new ApiException(error, response.StatusCode, exception)
        };

        async Task<(ApiErrorResult?, Exception?)> ReadApiErrorResultAsync()
        {
            try
            {
                // Get defensive here because I'm not sure that `Attr` is always a string, but I believe it be so.
#pragma warning disable CA2016
                var result = await response.Content.ReadFromJsonAsync<ApiErrorResult>(
                    cancellationToken: cancellationToken);
                return (result, null);
            }
            catch (JsonException e)
            {
                return (null, e);
            }
        }
    }
}