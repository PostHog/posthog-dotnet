using PostHog.Features;
using static PostHog.Library.Ensure;

namespace PostHog; // Intentionally put in the root namespace.

/// <summary>
/// Extensions of <see cref="IPostHogClient"/> specific to feature flag evaluation.
/// </summary>
public static class FeatureFlagExtensions
{
    /// <summary>
    /// Determines whether a feature is enabled for the specified user.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>
    /// <c>true</c> if the feature is enabled for the user. <c>false</c> if not. <c>null</c> if the feature does not
    /// exist.
    /// </returns>
    public static Task<bool?> IsFeatureEnabledAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId,
        CancellationToken cancellationToken)
        => NotNull(client).IsFeatureEnabledAsync(featureKey,
            distinctId,
            options: null, cancellationToken: cancellationToken);

    /// <summary>
    /// Determines whether a feature is enabled for the specified user.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <returns>
    /// <c>true</c> if the feature is enabled for the user. <c>false</c> if not. <c>null</c> if the feature is undefined.
    /// </returns>
    public static Task<bool?> IsFeatureEnabledAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId)
        => NotNull(client).IsFeatureEnabledAsync(featureKey,
            distinctId,
            options: null, cancellationToken: CancellationToken.None);

    /// <summary>
    /// Determines whether a feature is enabled for the specified user.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="options">Optional: Options used to control feature flag evaluation.</param>
    /// <returns>
    /// <c>true</c> if the feature is enabled for the user. <c>false</c> if not. <c>null</c> if the feature is undefined.
    /// </returns>
    public static Task<bool?> IsFeatureEnabledAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options)
        => NotNull(client).IsFeatureEnabledAsync(featureKey,
            distinctId,
            options, cancellationToken: CancellationToken.None);


    /// <summary>
    /// Retrieves a feature flag.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The feature flag or null if it does not exist or is not enabled.</returns>
    public static async Task<FeatureFlag?> GetFeatureFlagAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId,
        CancellationToken cancellationToken)
        => await NotNull(client).GetFeatureFlagAsync(featureKey,
            distinctId,
            options: null, cancellationToken: cancellationToken);

    /// <summary>
    /// Retrieves a feature flag.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <returns>The feature flag or null if it does not exist or is not enabled.</returns>
    public static async Task<FeatureFlag?> GetFeatureFlagAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId)
        => await NotNull(client).GetFeatureFlagAsync(
            featureKey,
            distinctId,
            options: null,
            cancellationToken: CancellationToken.None);

    /// <summary>
    /// Retrieves a feature flag.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="options">Optional: Options used to control feature flag evaluation.</param>
    /// <returns>The feature flag or null if it does not exist or is not enabled.</returns>
    public static async Task<FeatureFlag?> GetFeatureFlagAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId,
        FeatureFlagOptions options)
        => await NotNull(client).GetFeatureFlagAsync(featureKey,
            distinctId,
            options, cancellationToken: CancellationToken.None);

    /// <summary>
    /// Retrieves all the feature flags.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="options">Optional: Options used to control feature flag evaluation.</param>
    /// <returns>
    /// A dictionary containing all the feature flags. The key is the feature flag key and the value is the feature flag.
    /// </returns>
    public static async Task<IReadOnlyDictionary<string, FeatureFlag>> GetAllFeatureFlagsAsync(
        this IPostHogClient client,
        string distinctId,
        AllFeatureFlagsOptions options)
        => await NotNull(client)
            .GetAllFeatureFlagsAsync(distinctId, options, CancellationToken.None);

    /// <summary>
    /// Retrieves all the feature flags.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <returns>
    /// A dictionary containing all the feature flags. The key is the feature flag key and the value is the feature flag.
    /// </returns>
    public static async Task<IReadOnlyDictionary<string, FeatureFlag>> GetAllFeatureFlagsAsync(
        this IPostHogClient client,
        string distinctId)
    {
        return await NotNull(client)
            .GetAllFeatureFlagsAsync(distinctId, options: new AllFeatureFlagsOptions(), CancellationToken.None);
    }

    /// <summary>
    /// When reporting the result of a feature flag evaluation, this method converts the result to a string
    /// in a format expected by the Capture event api.
    /// </summary>
    /// <param name="featureFlag">The feature flag.</param>
    /// <returns>A string with either the variant key or true/false.</returns>
    internal static object ToResponseObject(this FeatureFlag? featureFlag)
        => featureFlag is not null
            ? featureFlag.VariantKey ?? (object)featureFlag.IsEnabled
            : "undefined";
}