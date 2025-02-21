using PostHog.Config;

namespace PostHog.FeatureManagement;

/// <summary>
/// Provides context used to evaluate feature flags. Use this when you want to supply person properties and groups
/// to PostHog for feature flag evaluation. To configure this, register an implementation of
/// <see cref="IPostHogFeatureFlagContextProvider"/> when calling
/// <see cref="PostHogConfigurationBuilderExtensions.UseFeatureManagement(IPostHogConfigurationBuilder, Action{IPostHogFeatureManagementBuilder})"/>
/// </summary>
public class PostHogFeatureFlagContext : FeatureFlagOptions
{
    /// <summary>
    /// The distinct identifier for the current user.
    /// </summary>
    public string? DistinctId { get; init; } = string.Empty;
}