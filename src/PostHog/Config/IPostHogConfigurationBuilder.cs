using Microsoft.Extensions.DependencyInjection;

namespace PostHog.Config;

/// <summary>
/// Base interface for configuring the PostHog client.
/// </summary>
public interface IPostHogConfigurationBuilder
{
    /// <summary>
    /// Calls the specified configuration action on the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="configurationAction">The configuration action to apply to <see cref="IServiceCollection"/>.</param>
    /// <returns>The <see cref="IPostHogConfigurationBuilder"/>.</returns>
    IPostHogConfigurationBuilder Use(Action<IServiceCollection> configurationAction);

    /// <summary>
    /// Allows the <see cref="HttpClient"/> used by <see cref="PostHogClient"/> to be additionally configured
    /// such as adding <see cref="HttpMessageHandler"/>s.
    /// </summary>
    /// <remarks>
    /// I was hoping I wouldn't have to special case this, but the AddHttpClient method creates and returns a new
    /// <see cref="DefaultHttpClientBuilder">DefaultHttpClientBuilder</see> which we need to store to be able to access.
    ///
    /// </remarks>
    /// <param name="configureHttpClient">An action used to configure the HttpClient via the <see cref="IHttpClientBuilder"/>.</param>
    /// <returns>The <see cref="IPostHogConfigurationBuilder"/>.</returns>
    IPostHogConfigurationBuilder ConfigureHttpClient(Action<IHttpClientBuilder> configureHttpClient);
}