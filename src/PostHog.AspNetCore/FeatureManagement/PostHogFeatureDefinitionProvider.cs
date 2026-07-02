using Microsoft.FeatureManagement;
using PostHog.Api;

namespace PostHog.FeatureManagement;

/// <summary>
/// Provides feature definitions so that PostHog feature flags works with ASP.NET Core feature management.
/// </summary>
/// <param name="posthog">The <see cref="IPostHogClient"/> used to evaluate feature flags.</param>
public class PostHogFeatureDefinitionProvider(IPostHogClient posthog) : IFeatureDefinitionProvider
{
    /// <inheritdoc />
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

    /// <inheritdoc />
    public async IAsyncEnumerable<FeatureDefinition> GetAllFeatureDefinitionsAsync()
    {
        var localEvaluator = await posthog.GetLocalEvaluatorAsync(CancellationToken.None);

        if (localEvaluator is not null)
        {
            foreach (var flag in localEvaluator.LocalEvaluationApiResult.Flags)
            {
                yield return CreateFeatureDefinition(flag.Key);
            }
            yield break;
        }

        // Fallback: no PersonalApiKey means no local-evaluation flag list. Poll /flags with a stable
        // sentinel distinct_id and use the returned keys as the enumeration source. We only care about
        // keys; the values for the sentinel are discarded. See PostHog/posthog-dotnet#64.
        foreach (var key in await FeatureEnumerationFallback.GetFeatureKeysAsync(posthog, CancellationToken.None))
        {
            yield return CreateFeatureDefinition(key);
        }
    }

    static FeatureDefinition CreateFeatureDefinition(string key)
    {
        return new FeatureDefinition
        {
            Name = key,
            EnabledFor = [new FeatureFilterConfiguration { Name = "PostHog" }],
            RequirementType = RequirementType.Any
        };
    }
}