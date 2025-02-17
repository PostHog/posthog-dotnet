using Microsoft.Extensions.Configuration;

namespace PostHog.Config;

public interface IPostHogAspNetCoreConfigurationBuilder : IPostHogConfigurationBuilder
{
    public IConfiguration Configuration { get; }
}