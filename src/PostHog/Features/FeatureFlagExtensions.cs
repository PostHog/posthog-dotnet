using PostHog.Features;
using PostHog.Json;
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
    [Obsolete("Prefer EvaluateFlagsAsync(distinctId).IsEnabled(featureKey). This method will be removed in a future major version.", error: false)]
    public static Task<bool> IsFeatureEnabledAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId,
        CancellationToken cancellationToken)
#pragma warning disable CS0618
        => NotNull(client).IsFeatureEnabledAsync(featureKey,
            distinctId,
            options: null, cancellationToken: cancellationToken);
#pragma warning restore CS0618

    /// <summary>
    /// Determines whether a feature is enabled for the specified user.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <returns>
    /// <c>true</c> if the feature is enabled for the user. <c>false</c> if not. <c>null</c> if the feature is undefined.
    /// </returns>
    [Obsolete("Prefer EvaluateFlagsAsync(distinctId).IsEnabled(featureKey). This method will be removed in a future major version.", error: false)]
    public static Task<bool> IsFeatureEnabledAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId)
#pragma warning disable CS0618
        => NotNull(client).IsFeatureEnabledAsync(featureKey,
            distinctId,
            options: null, cancellationToken: CancellationToken.None);
#pragma warning restore CS0618

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
    [Obsolete("Prefer EvaluateFlagsAsync(distinctId).IsEnabled(featureKey). This method will be removed in a future major version.", error: false)]
    public static Task<bool> IsFeatureEnabledAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId,
        FeatureFlagOptions? options)
#pragma warning disable CS0618
        => NotNull(client).IsFeatureEnabledAsync(featureKey,
            distinctId,
            options, cancellationToken: CancellationToken.None);
#pragma warning restore CS0618

    /// <summary>
    /// Retrieves a feature flag.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="personProperties">Optional: What person properties are known. Used to compute flags locally, if personalApiKey is present. Not needed if using remote evaluation, but can be used to override remote values for the purposes of feature flag evaluation.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The feature flag or null if it does not exist or is not enabled.</returns>
    [Obsolete("Prefer EvaluateFlagsAsync(distinctId, options).IsEnabled(featureKey). This method will be removed in a future major version.", error: false)]
    public static async Task<bool?> IsFeatureEnabledAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId,
        Dictionary<string, object?> personProperties,
        CancellationToken cancellationToken)
#pragma warning disable CS0618
        => await NotNull(client).IsFeatureEnabledAsync(
            featureKey,
            distinctId,
            new FeatureFlagOptions { PersonProperties = new Dictionary<string, object?>(personProperties) },
            cancellationToken);
#pragma warning restore CS0618

    /// <summary>
    /// Retrieves a feature flag.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="personProperties">Optional: What person properties are known. Used to compute flags locally, if personalApiKey is present. Not needed if using remote evaluation, but can be used to override remote values for the purposes of feature flag evaluation.</param>
    /// <returns>The feature flag or null if it does not exist or is not enabled.</returns>
    [Obsolete("Prefer EvaluateFlagsAsync(distinctId, options).IsEnabled(featureKey). This method will be removed in a future major version.", error: false)]
    public static async Task<bool?> IsFeatureEnabledAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId,
        Dictionary<string, object?> personProperties)
#pragma warning disable CS0618
        => await NotNull(client).IsFeatureEnabledAsync(
            featureKey,
            distinctId,
            personProperties,
            CancellationToken.None);
#pragma warning restore CS0618

    /// <summary>
    /// Retrieves a feature flag.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The feature flag or null if it does not exist or is not enabled.</returns>
    [Obsolete("Prefer EvaluateFlagsAsync(distinctId).GetFlag(featureKey). This method will be removed in a future major version.", error: false)]
    public static async Task<FeatureFlag?> GetFeatureFlagAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId,
        CancellationToken cancellationToken)
#pragma warning disable CS0618
        => await NotNull(client).GetFeatureFlagAsync(featureKey,
            distinctId,
            options: null, cancellationToken: cancellationToken);
#pragma warning restore CS0618

    /// <summary>
    /// Retrieves a feature flag.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <returns>The feature flag or null if it does not exist or is not enabled.</returns>
    [Obsolete("Prefer EvaluateFlagsAsync(distinctId).GetFlag(featureKey). This method will be removed in a future major version.", error: false)]
    public static async Task<FeatureFlag?> GetFeatureFlagAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId)
#pragma warning disable CS0618
        => await NotNull(client).GetFeatureFlagAsync(
            featureKey,
            distinctId,
            options: null,
            cancellationToken: CancellationToken.None);
#pragma warning restore CS0618

    /// <summary>
    /// Retrieves a feature flag.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="options">Optional: Options used to control feature flag evaluation.</param>
    /// <returns>The feature flag or null if it does not exist or is not enabled.</returns>
    [Obsolete("Prefer EvaluateFlagsAsync(distinctId, options).GetFlag(featureKey). This method will be removed in a future major version.", error: false)]
    public static async Task<FeatureFlag?> GetFeatureFlagAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId,
        FeatureFlagOptions options)
#pragma warning disable CS0618
        => await NotNull(client).GetFeatureFlagAsync(featureKey,
            distinctId,
            options, cancellationToken: CancellationToken.None);
#pragma warning restore CS0618

    /// <summary>
    /// Retrieves a feature flag.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="personProperties">Optional: What person properties are known. Used to compute flags locally, if personalApiKey is present. Not needed if using remote evaluation, but can be used to override remote values for the purposes of feature flag evaluation.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>The feature flag or null if it does not exist or is not enabled.</returns>
    [Obsolete("Prefer EvaluateFlagsAsync(distinctId, options).GetFlag(featureKey). This method will be removed in a future major version.", error: false)]
    public static async Task<FeatureFlag?> GetFeatureFlagAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId,
        Dictionary<string, object?> personProperties,
        CancellationToken cancellationToken)
#pragma warning disable CS0618
        => await NotNull(client).GetFeatureFlagAsync(
            featureKey,
            distinctId,
            new FeatureFlagOptions { PersonProperties = new Dictionary<string, object?>(personProperties) },
            cancellationToken);
#pragma warning restore CS0618

    /// <summary>
    /// Retrieves a feature flag.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="featureKey">The name of the feature flag.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="personProperties">Optional: What person properties are known. Used to compute flags locally, if personalApiKey is present. Not needed if using remote evaluation, but can be used to override remote values for the purposes of feature flag evaluation.</param>
    /// <returns>The feature flag or null if it does not exist or is not enabled.</returns>
    [Obsolete("Prefer EvaluateFlagsAsync(distinctId, options).GetFlag(featureKey). This method will be removed in a future major version.", error: false)]
    public static async Task<FeatureFlag?> GetFeatureFlagAsync(
        this IPostHogClient client,
        string featureKey,
        string distinctId,
        Dictionary<string, object?> personProperties)
#pragma warning disable CS0618
        => await NotNull(client).GetFeatureFlagAsync(
            featureKey,
            distinctId,
            personProperties,
            CancellationToken.None);
#pragma warning restore CS0618

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
    /// Evaluates all feature flags using the current request context distinct ID and returns a <see cref="FeatureFlagEvaluations"/> snapshot.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    public static Task<FeatureFlagEvaluations> EvaluateFlagsAsync(this IPostHogClient client)
        => NotNull(client).EvaluateFlagsAsync(
            PostHogContextHelper.ResolveDistinctId(distinctId: null) ?? string.Empty,
            options: null,
            CancellationToken.None);

    /// <summary>
    /// Evaluates all feature flags using the current request context distinct ID and returns a <see cref="FeatureFlagEvaluations"/> snapshot.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="options">Options used to control feature flag evaluation. <see cref="AllFeatureFlagsOptions.FlagKeysToEvaluate"/> scopes the underlying <c>/flags</c> request body.</param>
    public static Task<FeatureFlagEvaluations> EvaluateFlagsAsync(
        this IPostHogClient client,
        AllFeatureFlagsOptions options)
        => NotNull(client).EvaluateFlagsAsync(
            PostHogContextHelper.ResolveDistinctId(distinctId: null) ?? string.Empty,
            options,
            CancellationToken.None);

    /// <summary>
    /// Evaluates all feature flags for the user and returns a <see cref="FeatureFlagEvaluations"/> snapshot.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    public static Task<FeatureFlagEvaluations> EvaluateFlagsAsync(
        this IPostHogClient client,
        string distinctId)
        => NotNull(client).EvaluateFlagsAsync(distinctId, options: null, CancellationToken.None);

    /// <summary>
    /// Evaluates all feature flags for the user and returns a <see cref="FeatureFlagEvaluations"/> snapshot.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    public static Task<FeatureFlagEvaluations> EvaluateFlagsAsync(
        this IPostHogClient client,
        string distinctId,
        CancellationToken cancellationToken)
        => NotNull(client).EvaluateFlagsAsync(distinctId, options: null, cancellationToken);

    /// <summary>
    /// Evaluates all feature flags for the user and returns a <see cref="FeatureFlagEvaluations"/> snapshot.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="options">Options used to control feature flag evaluation. <see cref="AllFeatureFlagsOptions.FlagKeysToEvaluate"/> scopes the underlying <c>/flags</c> request body.</param>
    public static Task<FeatureFlagEvaluations> EvaluateFlagsAsync(
        this IPostHogClient client,
        string distinctId,
        AllFeatureFlagsOptions options)
        => NotNull(client).EvaluateFlagsAsync(distinctId, options, CancellationToken.None);

    /// <summary>
    /// Loads (or reloads) feature flag definitions for local evaluation.
    /// </summary>
    /// <remarks>
    /// This method forces a reload of feature flag definitions from the PostHog API and ensures
    /// that the polling mechanism is started for automatic updates. A personal API key is required
    /// for local evaluation. If no personal API key is configured, a warning will be logged.
    /// </remarks>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static Task LoadFeatureFlagsAsync(this IPostHogClient client)
        => NotNull(client).LoadFeatureFlagsAsync(CancellationToken.None);

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