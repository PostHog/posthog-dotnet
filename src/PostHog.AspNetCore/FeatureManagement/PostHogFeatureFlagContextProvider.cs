namespace PostHog.FeatureManagement;

/// <summary>
/// Base class used to provide context and options when evaluating PostHog feature flags by way
/// of .NET Feature Management.
/// </summary>
public abstract class PostHogFeatureFlagContextProvider : IPostHogFeatureFlagContextProvider
{
    /// <inheritdoc />
    public async ValueTask<PostHogFeatureFlagContext> GetContextAsync()
    {
        var options = await GetFeatureFlagOptionsAsync();

        return new PostHogFeatureFlagContext
        {
            DistinctId = GetDistinctId(),
            PersonProperties = options.PersonProperties,
            Groups = options.Groups,
            SendFeatureFlagEvents = options.SendFeatureFlagEvents,
            OnlyEvaluateLocally = options.OnlyEvaluateLocally
        };
    }

    /// <summary>
    /// Retrieves the options to control how feature flags are evaluated.
    /// </summary>
    /// <returns>A <see cref="FeatureFlagOptions"/>.</returns>
    protected virtual ValueTask<FeatureFlagOptions> GetFeatureFlagOptionsAsync() =>
        ValueTask.FromResult(new FeatureFlagOptions());

    /// <summary>
    /// Retrieves the distinct ID to use when evaluating feature flags.
    /// </summary>
    /// <returns>A distinct id used to identify the current user.</returns>
    protected abstract string? GetDistinctId();
}