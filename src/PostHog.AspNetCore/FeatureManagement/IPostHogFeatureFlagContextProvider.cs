namespace PostHog.FeatureManagement;

/// <summary>
/// Interface used to provide context and options when evaluating PostHog feature flags by way
/// of .NET Feature Management.
/// </summary>
/// <remarks>
/// Most applications will want to inherit from <see cref="PostHogFeatureFlagContextProvider"/>
/// rather than implement this interface directly.
/// </remarks>
public interface IPostHogFeatureFlagContextProvider
{
    /// <summary>
    /// Returns context information for evaluating feature flags.
    /// </summary>
    /// <returns>A <see cref="PostHogFeatureFlagContext"/>.</returns>
    ValueTask<PostHogFeatureFlagContext> GetContextAsync();
}