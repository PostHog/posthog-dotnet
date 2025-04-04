namespace PostHog;

internal class CapturedEventBatchContext(IFeatureFlagCache featureFlagCache)
{
    public IFeatureFlagCache FeatureFlagCache { get; } = featureFlagCache;
}