using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FeatureManagement;
using NSubstitute;
using PostHog.FeatureManagement;

namespace PostHogFeatureFilterTests;

public class TheEvaluateAsyncMethod
{
    [Fact]
    public async Task ReturnsNullIfContextNull()
    {
        var variantFeatureManager = Substitute.For<IVariantFeatureManager>();
        var filter = new PostHogFeatureFilter(variantFeatureManager, NullLogger<PostHogFeatureFilter>.Instance);

        var result = await filter.EvaluateAsync(null);

        Assert.False(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DelegatesToVariantFeatureManager(bool enabled)
    {
        var variantFeatureManager = Substitute.For<IVariantFeatureManager>();
        variantFeatureManager.IsEnabledAsync("FeatureName", CancellationToken.None).Returns(enabled);
        var context = new FeatureFilterEvaluationContext
        {
            FeatureName = "FeatureName"
        };
        var filter = new PostHogFeatureFilter(variantFeatureManager, NullLogger<PostHogFeatureFilter>.Instance);

        var result = await filter.EvaluateAsync(context);

        Assert.Equal(enabled, result);
    }
}