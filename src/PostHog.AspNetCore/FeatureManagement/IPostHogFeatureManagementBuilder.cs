using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;

namespace PostHog.FeatureManagement;

/// <summary>
/// Interface for configuring PostHog feature management services.
/// </summary>
public interface IPostHogFeatureManagementBuilder
{
    /// <summary>The application services.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Adds an <see cref="ISessionManager" /> to be used for storing feature state in a session.
    /// </summary>
    /// <typeparam name="T">An implementation of <see cref="ISessionManager" /></typeparam>
    /// <returns>The feature management builder.</returns>
    IPostHogFeatureManagementBuilder AddSessionManager<T>() where T : ISessionManager;
}