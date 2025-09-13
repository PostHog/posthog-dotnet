using PostHog.Json;

namespace PostHog.Api;

internal static class PostHogApiClientExtensions
{
    /// <summary>
    /// Identify a user with additional properties
    /// </summary>
    /// <param name="client">The <see cref="PostHogApiClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="personPropertiesToSet">
    /// Key value pairs to store as a property of the user. Any key value pairs in this dictionary that match
    /// existing property keys will overwrite those properties.
    /// </param>
    /// <param name="personPropertiesToSetOnce">User properties to set only once (ex: Sign up date). If a property already exists, then the
    /// value in this dictionary is ignored.
    /// </param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An <see cref="ApiResult"/> with the result of the operation.</returns>
    public static async Task<ApiResult> IdentifyAsync(
        this PostHogApiClient client,
        string distinctId,
        Dictionary<string, object>? personPropertiesToSet,
        Dictionary<string, object>? personPropertiesToSetOnce,
        CancellationToken cancellationToken)
    {
        var properties = new Dictionary<string, object>();

        if (personPropertiesToSet is not null)
        {
            properties["$set"] = personPropertiesToSet;
        }

        if (personPropertiesToSetOnce is not null)
        {
            properties["$set_once"] = personPropertiesToSetOnce;
        }

        return await client.SendEventAsync(
            distinctId,
            eventName: "$identify",
            properties,
            cancellationToken);
    }

    /// <summary>
    /// Identify a group with additional properties
    /// </summary>
    public static async Task<ApiResult> GroupIdentifyAsync(
        this PostHogApiClient client,
        string type,
        StringOrValue<int> key,
        Dictionary<string, object>? properties,
        CancellationToken cancellationToken,
        string? distinctId = null)
    {
        return await client.SendEventAsync(
            distinctId: distinctId ?? $"${type}_{key}",
            eventName: "$groupidentify",
            properties: new Dictionary<string, object>
            {
                ["$group_type"] = type,
                ["$group_key"] = key,
                ["$group_set"] = properties ?? new Dictionary<string, object>()
            },
            cancellationToken);
    }

    /// <summary>
    /// To marry up whatever a user does before they sign up or log in with what they do after you need to make an
    /// alias call. This will allow you to answer questions like "Which marketing channels leads to users churning
    /// after a month? or "What do users do on our website before signing up? In a purely back-end implementation, this
    /// means whenever an anonymous user does something, you'll want to send a session ID with the capture call.
    /// Then, when that users signs up, you want to do an alias call with the session ID and the newly created user ID.
    /// The same concept applies for when a user logs in. If you're using PostHog in the front-end and back-end,
    ///  doing the identify call in the frontend will be enough.
    /// </summary>
    /// <param name="client">The <see cref="PostHogApiClient"/>.</param>
    /// <param name="previousId">The anonymous or temporary identifier you were using for the user.</param>
    /// <param name="newId">The identifier for the known user. Typically a user id in your database.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An <see cref="ApiResult"/> with the result of the operation.</returns>
    public static async Task<ApiResult> AliasAsync(
        this PostHogApiClient client,
        string previousId,
        string newId,
        CancellationToken cancellationToken)
        => await client.SendEventAsync(previousId,
            "$create_alias",
            properties: new Dictionary<string, object>
            {
                ["distinct_id"] = previousId,
                ["alias"] = newId
            }, cancellationToken: cancellationToken);

    /// <summary>
    /// Unlink future events with the current user. Call this when a user logs out.
    /// </summary>
    public static async Task ResetAsync(
        this PostHogApiClient client,
        string distinctId,
        CancellationToken cancellationToken)
        => await client.SendEventAsync(
            distinctId,
            eventName: "$reset",
            properties: new Dictionary<string, object>(),
            cancellationToken);

    /// <summary>
    /// Capture an event with optional properties
    /// </summary>
    public static async Task<ApiResult> CaptureAsync(
        this PostHogApiClient client,
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties,
        CancellationToken cancellationToken)
    {
        properties ??= new Dictionary<string, object>();

        return await client.SendEventAsync(
            distinctId,
            eventName,
            properties,
            cancellationToken);
    }

    /// <summary>
    /// Capture an event with optional properties
    /// </summary>
    public static async Task<ApiResult> CaptureAsync(
        this PostHogApiClient client,
        string distinctId,
        string eventName) => await client.CaptureAsync(distinctId, eventName, null, CancellationToken.None);

    static async Task<ApiResult> SendEventAsync(
        this PostHogApiClient client,
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["event"] = eventName,
            ["distinct_id"] = distinctId
        };
        if (properties is not null)
        {
            payload["properties"] = properties;
        }

        return await client.SendEventAsync(payload, cancellationToken);
    }
}