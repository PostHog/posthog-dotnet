using Microsoft.Extensions.DependencyInjection;
using static PostHog.Library.Ensure;

namespace PostHog.Config;

/// <summary>
/// Extension method on <see cref="IServiceCollection"/> used to register the <see cref="PostHogClient"/> with
/// the dependency injection (DI) container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PostHogClient"/> as a singleton.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> where PostHog client dependencies are registered.</param>
    /// <returns>The <see cref="IServiceCollection"/> so additional calls can be chained.</returns>
    public static IServiceCollection AddPostHog(this IServiceCollection services)
        => services.AddPostHog(_ => { });

    /// <summary>
    /// Registers <see cref="PostHogClient"/> as a singleton.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> where PostHog client dependencies are registered.</param>
    /// <param name="options">An action used to provide additional configuration or services for the PostHog client.</param>
    /// <returns>The <see cref="IServiceCollection"/> so additional calls can be chained.</returns>
    public static IServiceCollection AddPostHog(
        this IServiceCollection services,
        Action<IPostHogConfigurationBuilder> options)
    {
        var builder = new PostHogConfigurationBuilder(services);
        NotNull(options)(builder);
        return builder.Build();
    }
}