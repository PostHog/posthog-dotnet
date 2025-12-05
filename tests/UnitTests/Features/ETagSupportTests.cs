using PostHog;
using PostHog.Library;
using UnitTests.Fakes;

namespace FeatureFlagsTests;

/// <summary>
/// Tests for ETag support in local evaluation feature flag polling.
/// These tests verify that the ETag infrastructure works correctly
/// when loading feature flags for local evaluation.
/// </summary>
public class ETagSupportTests
{
    const string LocalEvaluationResponse = """
        {
            "flags": [
                {
                    "key": "test-flag",
                    "active": true,
                    "rollout_percentage": 100,
                    "filters": {
                        "groups": [
                            {
                                "properties": [],
                                "rollout_percentage": 100
                            }
                        ]
                    }
                }
            ]
        }
        """;

    [Fact]
    public async Task FirstRequestDoesNotSendIfNoneMatchHeader()
    {
        var container = new TestContainer("fake-personal-api-key");
        var requestHandler = container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            LocalEvaluationResponse,
            "\"abc123\"");

        var client = container.Activate<PostHogClient>();

        await client.LoadFeatureFlagsAsync(CancellationToken.None);

        var request = requestHandler.ReceivedRequest;
        Assert.Empty(request.Headers.IfNoneMatch);
    }

    [Fact]
    public async Task SecondRequestSendsIfNoneMatchHeaderWithStoredETag()
    {
        var container = new TestContainer("fake-personal-api-key");

        // First request returns ETag
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            LocalEvaluationResponse,
            "\"etag-123\"");

        // Second request should include If-None-Match header
        var secondRequestHandler = container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            LocalEvaluationResponse,
            "\"etag-456\"");

        var client = container.Activate<PostHogClient>();

        // First load stores the ETag
        await client.LoadFeatureFlagsAsync(CancellationToken.None);

        // Second load should send If-None-Match
        await client.LoadFeatureFlagsAsync(CancellationToken.None);

        var secondRequest = secondRequestHandler.ReceivedRequest;
        Assert.Contains(secondRequest.Headers.IfNoneMatch, etag => etag.Tag == "\"etag-123\"");
    }

    [Fact]
    public async Task NotModifiedResponseReturnsCachedFlags()
    {
        var container = new TestContainer("fake-personal-api-key");

        // First request returns flags with ETag
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            LocalEvaluationResponse,
            "\"etag-123\"");

        // Second request returns 304 Not Modified
        container.FakeHttpMessageHandler.AddLocalEvaluationNotModifiedResponse("\"etag-123\"");

        // Need batch response for $feature_flag_called event
        container.FakeHttpMessageHandler.AddBatchResponse();

        var client = container.Activate<PostHogClient>();

        // First load
        await client.LoadFeatureFlagsAsync(CancellationToken.None);

        // Second load gets 304 - should still have flags from cache
        await client.LoadFeatureFlagsAsync(CancellationToken.None);

        // Verify flags still work from cached evaluator
        var result = await client.IsFeatureEnabledAsync("test-flag", "user-123");
        Assert.True(result);
    }

    [Fact]
    public async Task NotModifiedResponseWithoutETagPreservesOriginalETag()
    {
        var container = new TestContainer("fake-personal-api-key");

        // First request returns ETag
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            LocalEvaluationResponse,
            "\"original-etag\"");

        // 304 without ETag header (some servers don't return it)
        container.FakeHttpMessageHandler.AddLocalEvaluationNotModifiedResponse(etag: null);

        // Third request should still use the original ETag
        var thirdRequestHandler = container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            LocalEvaluationResponse,
            "\"new-etag\"");

        var client = container.Activate<PostHogClient>();

        await client.LoadFeatureFlagsAsync(CancellationToken.None); // Stores "original-etag"
        await client.LoadFeatureFlagsAsync(CancellationToken.None); // Gets 304 without ETag
        await client.LoadFeatureFlagsAsync(CancellationToken.None); // Should still send "original-etag"

        var thirdRequest = thirdRequestHandler.ReceivedRequest;
        Assert.Contains(thirdRequest.Headers.IfNoneMatch, etag => etag.Tag == "\"original-etag\"");
    }

    [Fact]
    public async Task QuotaLimitedErrorClearsETag()
    {
        var container = new TestContainer("fake-personal-api-key");

        // First request returns ETag
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            LocalEvaluationResponse,
            "\"etag-before-quota\"");

        // Second request returns quota_limited error
        container.FakeHttpMessageHandler.AddLocalEvaluationQuotaLimitedResponse();

        // Third request after quota restored - should NOT have If-None-Match (ETag was cleared)
        var thirdRequestHandler = container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            LocalEvaluationResponse,
            "\"etag-after-quota\"");

        var client = container.Activate<PostHogClient>();

        await client.LoadFeatureFlagsAsync(CancellationToken.None); // Stores ETag

        // This should throw quota_limited error and clear ETag
        await Assert.ThrowsAsync<ApiException>(
            () => client.LoadFeatureFlagsAsync(CancellationToken.None));

        // Next request should not have If-None-Match header (ETag was cleared)
        await client.LoadFeatureFlagsAsync(CancellationToken.None);

        var thirdRequest = thirdRequestHandler.ReceivedRequest;
        Assert.Empty(thirdRequest.Headers.IfNoneMatch);
    }

    [Fact]
    public async Task ClearLocalFlagsCacheClearsETag()
    {
        var container = new TestContainer("fake-personal-api-key");

        // First request returns ETag
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            LocalEvaluationResponse,
            "\"etag-before-clear\"");

        // Second request after clear - should not have If-None-Match
        var secondRequestHandler = container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            LocalEvaluationResponse,
            "\"etag-after-clear\"");

        var client = container.Activate<PostHogClient>();

        await client.LoadFeatureFlagsAsync(CancellationToken.None); // Stores ETag

        client.ClearLocalFlagsCache(); // Should clear ETag

        await client.LoadFeatureFlagsAsync(CancellationToken.None); // Fresh request

        var secondRequest = secondRequestHandler.ReceivedRequest;
        Assert.Empty(secondRequest.Headers.IfNoneMatch);
    }

    [Fact]
    public async Task ResponseWithETagStillReturnsFlags()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            LocalEvaluationResponse,
            "\"test-etag\"");

        // Need batch response for $feature_flag_called event
        container.FakeHttpMessageHandler.AddBatchResponse();

        var client = container.Activate<PostHogClient>();

        await client.LoadFeatureFlagsAsync(CancellationToken.None);

        // Verify flag is available (proving response was processed correctly)
        var result = await client.IsFeatureEnabledAsync("test-flag", "user-123");
        Assert.True(result);
    }

    [Fact]
    public async Task ResponseWithoutETagStillReturnsFlags()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(LocalEvaluationResponse);

        // Need batch response for $feature_flag_called event
        container.FakeHttpMessageHandler.AddBatchResponse();

        var client = container.Activate<PostHogClient>();

        await client.LoadFeatureFlagsAsync(CancellationToken.None);

        // Verify flag is available
        var result = await client.IsFeatureEnabledAsync("test-flag", "user-123");
        Assert.True(result);
    }
}
