using System.Text.Json;
using static PostHog.Library.Ensure;

namespace PostHog;

/// <summary>
/// Extensions of <see cref="IPostHogClient"/> related to remote config.
/// </summary>
public static class RemoteConfigExtensions
{
    /// <summary>
    /// Retrieves a remote config payload.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/> to extend.</param>
    /// <param name="key">The remote config key.</param>
    /// <returns>The <see cref="JsonDocument"/> payload for the remote config setting.</returns>
    public static async Task<JsonDocument?> GetRemoteConfigPayloadAsync(this IPostHogClient client, string key) =>
        await NotNull(client).GetRemoteConfigPayloadAsync(key, CancellationToken.None);
}