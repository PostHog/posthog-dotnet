using System.Net;
using System.Net.Http.Json;
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
        var response = await httpClient.PostAsJsonAsync(
            requestUri,
            content,
            JsonSerializerHelper.Options,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            var error = await response.Content.ReadFromJsonAsync<UnauthorizedApiResult>(
                cancellationToken: cancellationToken);
            throw new UnauthorizedAccessException(error?.Detail);
        }
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializerHelper.DeserializeFromCamelCaseJsonAsync<TBody>(
            result,
            cancellationToken: cancellationToken);
    }
}