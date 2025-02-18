using PostHog;
using PostHog.FeatureManagement;

namespace HogTied.Web.FeatureManagement;

/// <summary>
/// Provides context and options used to evaluate feature flags for the HogTied application.
/// </summary>
/// <param name="httpContextAccessor">The <see cref="IHttpContextAccessor"/> used to get the current user's Id.</param>
public class HogTiedFeatureFlagContextProvider(IHttpContextAccessor httpContextAccessor)
    : PostHogFeatureFlagContextProvider
{
    protected override string? GetDistinctId() =>
        httpContextAccessor.HttpContext?.User.Identity?.Name;

    protected override ValueTask<FeatureFlagOptions> GetFeatureFlagOptionsAsync()
    {
        // In a real app, you might get this information from a database or other source for the current user.
        return ValueTask.FromResult(
            new FeatureFlagOptions
            {
                PersonProperties = new Dictionary<string, object?>
                {
                    ["email"] = "some-test@example.com"
                },
                Groups = [
                    new Group("project", "MAYHEM")
                    {
                        ["is_demo"] = true
                    }
                ],
                OnlyEvaluateLocally = true
            });
    }
}