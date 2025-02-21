using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;

namespace PostHog.FeatureManagement;

/// <summary>
/// Implementation of the <see cref="IPostHogFeatureManagementBuilder"/> interface.
/// </summary>
/// <param name="builder"></param>
internal sealed class PostHogFeatureManagementBuilder(IFeatureManagementBuilder builder)
    : IPostHogFeatureManagementBuilder
{
    public IServiceCollection Services => builder.Services;

    /// <inheritdoc />
    public IPostHogFeatureManagementBuilder AddSessionManager<T>() where T : ISessionManager
    {
        builder.AddSessionManager<T>();
        return this;
    }
}