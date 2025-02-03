using PostHog.Api;
using PostHog.Json;
using static PostHog.Library.Ensure;

namespace PostHog; // Intentionally put in the root namespace.

public static class IdentifyGroupAsyncExtensions
{
    /// <summary>
    /// Sets a groups properties, which allows asking questions like "Who are the most active companies"
    /// using my product in PostHog.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="type">Type of group (ex: 'company'). Limited to 5 per project</param>
    /// <param name="key">Unique identifier for that type of group (ex: 'id:5')</param>
    /// <param name="name">The friendly name of the group.</param>
    /// <param name="properties">Additional information about the group.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An <see cref="ApiResult"/> with the result of the operation.</returns>
    public static async Task<ApiResult> IdentifyGroupAsync(
        this IPostHogClient client,
        string type,
        StringOrValue<int> key,
        string name,
        Dictionary<string, object>? properties,
        CancellationToken cancellationToken)
    {
        properties ??= new Dictionary<string, object>();
        properties["name"] = name;
        return await NotNull(client).IdentifyGroupAsync(type, key, properties, cancellationToken);
    }

    /// <summary>
    /// Sets a groups properties, which allows asking questions like "Who are the most active companies"
    /// using my product in PostHog.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="type">Type of group (ex: 'company'). Limited to 5 per project</param>
    /// <param name="key">Unique identifier for that type of group (ex: 'id:5')</param>
    /// <param name="name">The friendly name of the group.</param>
    /// <returns>An <see cref="ApiResult"/> with the result of the operation.</returns>
    public static async Task<ApiResult> IdentifyGroupAsync(
        this IPostHogClient client,
        string type,
        StringOrValue<int> key,
        string name)
        => await IdentifyGroupAsync(
            client,
            type,
            key,
            name,
            properties: new Dictionary<string, object>(),
            CancellationToken.None);
}