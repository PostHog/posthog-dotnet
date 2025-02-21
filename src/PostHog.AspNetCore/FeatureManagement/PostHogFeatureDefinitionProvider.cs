using Microsoft.FeatureManagement;
using PostHog.Api;

namespace PostHog.FeatureManagement;

/// <summary>
/// Provides feature definitions so that PostHog feature flags works with ASP.NET Core feature management.
/// </summary>
/// <param name="posthog">The <see cref="IPostHogClient"/> used to evaluate feature flags.</param>
public class PostHogFeatureDefinitionProvider(IPostHogClient posthog) : IFeatureDefinitionProvider
{
    public async Task<FeatureDefinition?> GetFeatureDefinitionAsync(string featureName)
    {
        await foreach (var feature in GetAllFeatureDefinitionsAsync())
        {
            if (string.Equals(feature.Name, featureName, StringComparison.OrdinalIgnoreCase))
            {
                return feature;
            }
        }
        return null;
    }

    public async IAsyncEnumerable<FeatureDefinition> GetAllFeatureDefinitionsAsync()
    {
        var localEvaluator = await posthog.GetLocalEvaluatorAsync(CancellationToken.None);

        if (localEvaluator is not null)
        {
            foreach (var flag in localEvaluator.LocalEvaluationApiResult.Flags)
            {
                yield return CreateFeatureDefinition(flag);
            }
        }
    }

    static FeatureDefinition CreateFeatureDefinition(LocalFeatureFlag flag)
    {
        return new FeatureDefinition
        {
            Name = flag.Key,
            EnabledFor = [new FeatureFilterConfiguration { Name = "PostHog" }],
            RequirementType = RequirementType.Any
        };
    }
}