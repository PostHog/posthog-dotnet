using Microsoft.FeatureManagement.FeatureFilters;
using static PostHog.Library.Ensure;

namespace PostHog.FeatureManagement;

/// <summary>
/// Implements a <see cref="TargetingContext"/> that works for PostHog feature flag evaluation.
/// This wraps a <see cref="PostHogFeatureFlagContext"/> which is what consumers of the PostHog library will
/// set up.
/// </summary>
internal sealed class PostHogTargetingContext : TargetingContext
{
    readonly PostHogFeatureFlagContext _context;

    public PostHogTargetingContext(PostHogFeatureFlagContext context)
    {
        _context = NotNull(context);

        UserId = _context.DistinctId;
        Groups = _context.Groups?.Select(g => g.GroupKey);
    }

    public Dictionary<string, object?>? PersonProperties => _context.PersonProperties;

    public GroupCollection? GroupCollection => _context.Groups;


}