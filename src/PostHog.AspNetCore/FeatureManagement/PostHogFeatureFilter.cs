using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace PostHog.FeatureManagement;

/// <summary>
/// PostHog <see cref="IFeatureFilter"/> that uses PostHog's API to check if a feature is enabled for the current user.
/// </summary>
/// <param name="variantFeatureManager">The <see cref="IVariantFeatureManager"/> we'll use to evaluate the flag.</param>
[FilterAlias("PostHog")]
public class PostHogFeatureFilter(IVariantFeatureManager variantFeatureManager, ILogger<PostHogFeatureFilter> logger)
    : IFeatureFilter
{
    /// <summary>
    /// Evaluates whether the feature is enabled for the current user.
    /// </summary>
    /// <param name="context">Provides context such as the feature name.</param>
    /// <returns>
    /// A <see cref="Task"/> that evaluates to <c>true</c> if the feature is enabled, otherwise <c>false</c>
    /// </returns>
    public async Task<bool> EvaluateAsync(FeatureFilterEvaluationContext? context)
    {
        if (context is null)
        {
            return false;
        }

        if (variantFeatureManager is not PostHogVariantFeatureManager)
        {
            logger.LogWarningVariantFeatureManagerNotRegistered(variantFeatureManager.GetType());
        }

        return await variantFeatureManager.IsEnabledAsync(context.FeatureName, CancellationToken.None);
    }
}

internal static partial class PostHogFeatureFilterLoggerExtensions
{
    [LoggerMessage(
        EventId = 10000,
        Level = LogLevel.Warning,
        Message = "The IVariantFeatureManager provided to the PostHogFeatureFilter is not a PostHogVariantFeatureManager (it is a {Type}). The filter will not work as expected.")]
    public static partial void LogWarningVariantFeatureManagerNotRegistered(
        this ILogger<PostHogFeatureFilter> logger,
        Type type);
}