using Microsoft.Extensions.Configuration;

namespace PostHog.Config;

/// <summary>
/// Interface for the configuration builder used to configure PostHog services for ASP.NET Core.
/// </summary>
public interface IPostHogAspNetCoreConfigurationBuilder : IPostHogConfigurationBuilder
{
    /// <summary>
    /// Gets the ASP.NET Core application configuration.
    /// </summary>
    public IConfiguration Configuration { get; }
}