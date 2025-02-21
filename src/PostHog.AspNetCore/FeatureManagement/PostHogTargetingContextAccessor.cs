using Microsoft.FeatureManagement.FeatureFilters;

namespace PostHog.FeatureManagement;

/// <summary>
/// Implementation of a <see cref="ITargetingContextAccessor"/> specific to PostHog.
/// </summary>
/// <remarks>
/// This is internal because consumers will use <see cref="IPostHogFeatureManagementBuilder"/> to
/// register a <see cref="IPostHogFeatureFlagContextProvider"/> which this uses.
/// </remarks>
/// <param name="featureFlagContextProvider">
/// The <see cref="IPostHogFeatureFlagContextProvider"/> consumers will set up.
/// </param>
#pragma warning disable CA1812
internal sealed class PostHogTargetingContextAccessor(IPostHogFeatureFlagContextProvider featureFlagContextProvider)
    : ITargetingContextAccessor
{
    public async ValueTask<TargetingContext> GetContextAsync()
    {
        var context = await featureFlagContextProvider.GetContextAsync();
        return new PostHogTargetingContext(context);
    }
}
#pragma warning restore CA1812