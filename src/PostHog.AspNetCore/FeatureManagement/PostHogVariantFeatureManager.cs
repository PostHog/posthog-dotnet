using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;
using PostHog.Features;

namespace PostHog.FeatureManagement;

/// <summary>
/// Implements PostHog variant support for ASP.NET Core's Feature Management system.
/// </summary>
/// <param name="posthog">The <see cref="IPostHogClient"/> used to evaluate feature flags.</param>
/// <param name="targetingContextAccessor">The <see cref="ITargetingContextAccessor" /> used to evaluate feature flags.</param>
public class PostHogVariantFeatureManager(
    IPostHogClient posthog,
    ITargetingContextAccessor targetingContextAccessor,
    ILogger<PostHogVariantFeatureManager> logger)
    : IVariantFeatureManager
{
    public async IAsyncEnumerable<string> GetFeatureNamesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = new())
    {
        var localEvaluator = await posthog.GetLocalEvaluatorAsync(cancellationToken);
        if (localEvaluator is null)
        {
            yield break;
        }
        foreach (var flag in localEvaluator.LocalEvaluationApiResult.Flags)
        {
            yield return flag.Key;
        }
    }

    public async ValueTask<bool> IsEnabledAsync(string feature, CancellationToken cancellationToken = new())
    {
        var targetingContext = await targetingContextAccessor.GetContextAsync();

        return await IsEnabledAsync<TargetingContext>(feature, targetingContext, cancellationToken);
    }

    public async ValueTask<bool> IsEnabledAsync<TContext>(
        string feature,
        TContext context,
        CancellationToken cancellationToken = new())
    {
        var flag = await GetFeatureFlagAsync(feature, context as ITargetingContext, cancellationToken);
        return flag is { IsEnabled: true };
    }

    public async ValueTask<Variant> GetVariantAsync(string feature, CancellationToken cancellationToken = new())
    {
        var targetingContext = await targetingContextAccessor.GetContextAsync();
        return await GetVariantAsync(feature, targetingContext, cancellationToken);
    }

    public async ValueTask<Variant> GetVariantAsync(
        string feature,
        ITargetingContext? context,
        CancellationToken cancellationToken = new())
    {
        var flag = await GetFeatureFlagAsync(feature, context, cancellationToken);
        return new Variant { Name = flag?.VariantKey };
    }

    async ValueTask<FeatureFlag?> GetFeatureFlagAsync(
        string feature,
        ITargetingContext? context,
        CancellationToken cancellationToken)
    {
        if (context?.UserId is null)
        {
            return new FeatureFlag
            {
                Key = feature,
                IsEnabled = false
            };
        }

        if (context is not PostHogTargetingContext)
        {
            logger.LogWarningVariantFeatureManagerNotRegistered(context.GetType());
        }

        var (personProperties, groups) = context is PostHogTargetingContext postHogTargetingContext
            ? (postHogTargetingContext.PersonProperties, GroupsAndProperties: postHogTargetingContext.GroupCollection)
            : (null, null);

        // Call PostHog's API to check if the feature is enabled for this user
        return await posthog.GetFeatureFlagAsync(
            featureKey: feature,
            distinctId: context.UserId,
            options: new FeatureFlagOptions
            {
                PersonProperties = personProperties,
                Groups = groups,
            },
            cancellationToken
        );
    }
}

internal static partial class PostHogVariantFeatureManagerLoggerExtensions
{
    [LoggerMessage(
        EventId = 10000,
        Level = LogLevel.Warning,
        Message = "The ITargetingContextAccessor provided to the PostHogVariantFeatureManager is not a PostHogTargetingContextAccessor (it is a {Type}). The filter will not work as expected.")]
    public static partial void LogWarningVariantFeatureManagerNotRegistered(
        this ILogger<PostHogVariantFeatureManager> logger,
        Type type);
}