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
    /// Returns the response body deserialized as <typeparamref name="TBody"/> using the provided JSON serializer.
    /// </summary>
    /// <param name="httpClient">The client used to send the request.</param>
    /// <param name="requestUri">The Uri the request is sent to.</param>
    /// <param name="content">The value to serialize.</param>
    /// <param name="jsonSerializer">The JSON serializer wrapper to use for deserialization.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <typeparam name="TBody">The type of the response body to deserialize to.</typeparam>
    /// <returns>The task representing the asynchronous operation.</returns>
    public static async Task<TBody?> PostJsonAsync<TBody>(
        this HttpClient httpClient,
        Uri requestUri,
        object content,
        JsonSerializerWrapper jsonSerializer,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            requestUri,
            content,
            JsonSerializerWrapper.Options,
            cancellationToken);

        await response.EnsureSuccessfulApiCall(jsonSerializer, cancellationToken);

        var result = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await jsonSerializer.DeserializeFromCamelCaseJsonAsync<TBody>(
            result,
            cancellationToken: cancellationToken);
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

    public static async Task EnsureSuccessfulApiCall(
        this HttpResponseMessage response,
        JsonSerializerWrapper jsonSerializer,
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
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var result = await jsonSerializer.DeserializeFromCamelCaseJsonAsync<ApiErrorResult>(stream, cancellationToken);
                return (result, null);
            }
            catch (JsonException e)
            {
                return (null, e);
            }
        }
    }
}