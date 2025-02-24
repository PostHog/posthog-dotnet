using PostHog.Api;
using static PostHog.Library.Ensure;

namespace PostHog; // Intentionally put in the root namespace.

/// <summary>
/// Extension methods for identifying a user.
/// </summary>
public static class IdentifyPersonAsyncExtensions
{
    /// <summary>
    /// Identifies a user with the specified distinct ID and user properties.
    /// See <seealso href="https://posthog.com/docs/getting-started/identify-users"/>.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    public static Task<ApiResult> IdentifyAsync(this IPostHogClient client, string distinctId)
        => NotNull(client).IdentifyAsync(distinctId, CancellationToken.None);

    /// <summary>
    /// Identifies a user with the specified distinct ID and user properties.
    /// See <seealso href="https://posthog.com/docs/getting-started/identify-users"/>.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    public static Task<ApiResult> IdentifyAsync(
        this IPostHogClient client,
        string distinctId,
        CancellationToken cancellationToken)
        => NotNull(client)
            .IdentifyAsync(
            distinctId,
            personPropertiesToSet: null,
            personPropertiesToSetOnce: null,
            cancellationToken);

    /// <summary>
    /// Identifies a user with the specified distinct ID and user properties.
    /// See <seealso href="https://posthog.com/docs/getting-started/identify-users"/>.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="email">An email to associate with the person.</param>
    /// <param name="name">The person's name.</param>
    public static Task<ApiResult> IdentifyAsync(
        this IPostHogClient client,
        string distinctId,
        string? email,
        string? name)
        => client.IdentifyAsync(
            distinctId,
            email,
            name,
            CancellationToken.None);

    /// <summary>
    /// Identifies a user with the specified distinct ID and user properties.
    /// See <seealso href="https://posthog.com/docs/getting-started/identify-users"/>.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="email">An email to associate with the person.</param>
    /// <param name="name">The person's name.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    public static Task<ApiResult> IdentifyAsync(
        this IPostHogClient client,
        string distinctId,
        string? email,
        string? name,
        CancellationToken cancellationToken)
        => client.IdentifyAsync(
            distinctId,
            email,
            name,
            personPropertiesToSet: null,
            cancellationToken);

    /// <summary>
    /// Identifies a user with the specified distinct ID and user properties.
    /// See <seealso href="https://posthog.com/docs/getting-started/identify-users"/>.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="email">The email to set for the user.</param>
    /// <param name="name">The name to set for the user</param>
    /// <param name="personPropertiesToSet">
    /// Key value pairs to store as properties of the user in addition to the already specified "email" and "name".
    /// Any key value pairs in this dictionary that match existing property keys will overwrite those properties.
    /// </param>
    public static async Task<ApiResult> IdentifyAsync(
        this IPostHogClient client,
        string distinctId,
        string? email,
        string? name,
        Dictionary<string, object>? personPropertiesToSet)
        => await client.IdentifyAsync(
            distinctId,
            email,
            name,
            personPropertiesToSet,
            personPropertiesToSetOnce: null);

    /// <summary>
    /// Identifies a user with the specified distinct ID and user properties.
    /// See <seealso href="https://posthog.com/docs/getting-started/identify-users"/>.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="email">The email to set for the user.</param>
    /// <param name="name">The name to set for the user</param>
    /// <param name="personPropertiesToSet">
    /// Key value pairs to store as properties of the user in addition to the already specified "email" and "name".
    /// Any key value pairs in this dictionary that match existing property keys will overwrite those properties.
    /// </param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    public static async Task<ApiResult> IdentifyAsync(
        this IPostHogClient client,
        string distinctId,
        string? email,
        string? name,
        Dictionary<string, object>? personPropertiesToSet,
        CancellationToken cancellationToken)
        => await client.IdentifyAsync(
            distinctId,
            email,
            name,
            personPropertiesToSet,
            personPropertiesToSetOnce: null,
            cancellationToken);

    /// <summary>
    /// Identifies a user with the specified distinct ID and user properties.
    /// See <seealso href="https://posthog.com/docs/getting-started/identify-users"/>.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="email">The email to set for the user.</param>
    /// <param name="name">The name to set for the user</param>
    /// <param name="personPropertiesToSet">
    /// Key value pairs to store as properties of the user in addition to the already specified "email" and "name".
    /// Any key value pairs in this dictionary that match existing property keys will overwrite those properties.
    /// </param>
    /// <param name="personPropertiesToSetOnce">User properties to set only once (ex: Sign up date). If a property already exists, then the
    /// value in this dictionary is ignored.
    /// </param>
    public static async Task<ApiResult> IdentifyAsync(
        this IPostHogClient client,
        string distinctId,
        string? email,
        string? name,
        Dictionary<string, object>? personPropertiesToSet,
        Dictionary<string, object>? personPropertiesToSetOnce)
        => await client.IdentifyAsync(
            distinctId,
            email,
            name,
            personPropertiesToSet,
            personPropertiesToSetOnce: personPropertiesToSetOnce,
            CancellationToken.None);

    /// <summary>
    /// Identifies a user with the specified distinct ID and user properties.
    /// See <seealso href="https://posthog.com/docs/getting-started/identify-users"/>.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="email">The email to set for the user.</param>
    /// <param name="name">The name to set for the user</param>
    /// <param name="personPropertiesToSet">
    /// Key value pairs to store as properties of the user in addition to the already specified "email" and "name".
    /// Any key value pairs in this dictionary that match existing property keys will overwrite those properties.
    /// </param>
    /// <param name="personPropertiesToSetOnce">User properties to set only once (ex: Sign up date). If a property already exists, then the
    /// value in this dictionary is ignored.
    /// </param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    public static async Task<ApiResult> IdentifyAsync(
        this IPostHogClient client,
        string distinctId,
        string? email,
        string? name,
        Dictionary<string, object>? personPropertiesToSet,
        Dictionary<string, object>? personPropertiesToSetOnce,
        CancellationToken cancellationToken)
    {
        if (email is not null)
        {
            personPropertiesToSet ??= new Dictionary<string, object>();
            personPropertiesToSet["email"] = email;
        }

        if (name is not null)
        {
            personPropertiesToSet ??= new Dictionary<string, object>();
            personPropertiesToSet["name"] = name;
        }

        return await NotNull(client).IdentifyAsync(distinctId,
                personPropertiesToSet,
                personPropertiesToSetOnce,
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
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="previousId">The anonymous or temporary identifier you were using for the user.</param>
    /// <param name="newId">The identifier for the known user. Typically a user id in your database.</param>
    /// <returns>An <see cref="ApiResult"/> with the result of the operation.</returns>
    public static async Task<ApiResult> AliasAsync(
        this IPostHogClient client,
        string previousId,
        string newId)
        => await NotNull(client).AliasAsync(previousId, newId, CancellationToken.None);
}