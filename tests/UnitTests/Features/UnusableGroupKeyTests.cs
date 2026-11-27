using System.Text.Json;
using PostHog;
using PostHog.Api;
using PostHog.Exceptions;
using PostHog.Features;
using PostHog.Json;
using UnitTests.Fakes;

namespace UnusableGroupKeyTests;

// A usable group key is a non-empty string. Group.GroupKey is typed as string, so the empty string and null are the
// only unusable keys a caller can pass. Booleans, arrays and objects can't be passed through this API.
static class UnusableGroupKeys
{
    public static TheoryData<string?> All => new() { "", null };

    public static GroupCollection Organization(string? groupKey) =>
        new() { new Group("organization", groupKey!) };

    public const string Definitions = """
        {"flags": [
            {"key": "org-flag", "active": true, "filters": {"aggregation_group_type_index": 0, "groups": [
                {"properties": [], "rollout_percentage": 100}
            ]}},
            {"key": "mixed-flag", "active": true, "filters": {"groups": [
                {"aggregation_group_type_index": 0, "properties": [], "rollout_percentage": 100},
                {"properties": [{"key": "plan", "type": "person", "value": "pro", "operator": "exact"}], "rollout_percentage": 100}
            ]}},
            {"key": "dependent-flag", "active": true, "filters": {"groups": [{"properties": [
                {"key": "org-flag", "type": "flag", "value": true, "operator": "flag_evaluates_to", "dependency_chain": ["org-flag"]}
            ], "rollout_percentage": 100}]}}
        ],
         "group_type_mapping": {"0": "organization"}}
        """;
}

public class TheLocalEvaluator
{
    static LocalEvaluator CreateEvaluator(string definitionsJson) =>
        new(JsonSerializer.Deserialize<LocalEvaluationApiResult>(definitionsJson, JsonSerializerHelper.Options)!);

    [Theory]
    [MemberData(nameof(UnusableGroupKeys.All), MemberType = typeof(UnusableGroupKeys))]
    public void GroupFlagWithAnUnusableKeyIsFalse(string? groupKey)
    {
        var evaluator = CreateEvaluator(UnusableGroupKeys.Definitions);

        var result = evaluator.EvaluateFeatureFlag("org-flag", "user", UnusableGroupKeys.Organization(groupKey));

        Assert.False(result.Value);
    }

    [Theory]
    [MemberData(nameof(UnusableGroupKeys.All), MemberType = typeof(UnusableGroupKeys))]
    public void MixedFlagSkipsTheGroupConditionAndThePersonConditionDecides(string? groupKey)
    {
        var evaluator = CreateEvaluator(UnusableGroupKeys.Definitions);
        var groups = UnusableGroupKeys.Organization(groupKey);

        var freePlan = evaluator.EvaluateFeatureFlag(
            "mixed-flag", "user", groups, new Dictionary<string, object?> { ["plan"] = "free" });
        var proPlan = evaluator.EvaluateFeatureFlag(
            "mixed-flag", "user", groups, new Dictionary<string, object?> { ["plan"] = "pro" });

        Assert.False(freePlan.Value);
        Assert.True(proPlan.Value);
    }

    [Theory]
    [MemberData(nameof(UnusableGroupKeys.All), MemberType = typeof(UnusableGroupKeys))]
    public void GroupDependencyWithAnUnusableKeyIsFalse(string? groupKey)
    {
        var evaluator = CreateEvaluator(UnusableGroupKeys.Definitions);

        var result = evaluator.EvaluateFeatureFlag("dependent-flag", "user", UnusableGroupKeys.Organization(groupKey));

        Assert.False(result.Value);
    }

    [Theory]
    [MemberData(nameof(UnusableGroupKeys.All), MemberType = typeof(UnusableGroupKeys))]
    public void EvaluateAllFlagsResolvesUnusableKeysWithoutFallback(string? groupKey)
    {
        var evaluator = CreateEvaluator(UnusableGroupKeys.Definitions);

        var (results, fallbackToRemote, unresolved) = evaluator.EvaluateAllFlags(
            "user",
            UnusableGroupKeys.Organization(groupKey),
            new Dictionary<string, object?> { ["plan"] = "free" });

        Assert.False(results["org-flag"].IsEnabled);
        Assert.False(results["mixed-flag"].IsEnabled);
        Assert.False(results["dependent-flag"].IsEnabled);
        Assert.False(fallbackToRemote);
        Assert.Empty(unresolved);
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("42")]
    public void UsableKeyStillMatchesAndBucketsByTheGroupKey(string groupKey)
    {
        var evaluator = CreateEvaluator(UnusableGroupKeys.Definitions);
        Assert.True(evaluator.EvaluateFeatureFlag("org-flag", "user", UnusableGroupKeys.Organization(groupKey)).Value);

        // A 50% group flag buckets the group key exactly like a 50% person flag with the same key buckets that
        // value as a distinct id.
        var groupFlag = CreateEvaluator("""
            {"flags": [{"key": "rollout-flag", "active": true, "filters": {"aggregation_group_type_index": 0, "groups": [
                {"properties": [], "rollout_percentage": 50}
            ]}}],
             "group_type_mapping": {"0": "organization"}}
            """);
        var personFlag = CreateEvaluator("""
            {"flags": [{"key": "rollout-flag", "active": true, "filters": {"groups": [
                {"properties": [], "rollout_percentage": 50}
            ]}}]}
            """);
        var results = new HashSet<bool>();
        for (var i = 0; i < 20; i++)
        {
            var key = $"{groupKey}-{i}";
            var grouped = groupFlag.EvaluateFeatureFlag("rollout-flag", "user", UnusableGroupKeys.Organization(key)).Value;
            Assert.Equal(personFlag.EvaluateFeatureFlag("rollout-flag", key).Value, grouped);
            results.Add(grouped == true);
        }
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void UsableKeyWithoutTheNeededGroupPropertiesStaysInconclusive()
    {
        var evaluator = CreateEvaluator("""
            {"flags": [{"key": "tier-flag", "active": true, "filters": {"aggregation_group_type_index": 0, "groups": [
                {"properties": [{"key": "tier", "type": "group", "group_type_index": 0, "value": "gold", "operator": "exact"}], "rollout_percentage": 100}
            ]}}],
             "group_type_mapping": {"0": "organization"}}
            """);

        Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("tier-flag", "user", UnusableGroupKeys.Organization("acme")));
    }
}

public class ThePostHogClient
{
    // The /flags response disagrees with the expected local result, so a request would show in the result.
    const string FlagsResponse = """{"featureFlags": {"org-flag": true, "mixed-flag": true, "dependent-flag": true}}""";

    [Theory]
    [MemberData(nameof(UnusableGroupKeys.All), MemberType = typeof(UnusableGroupKeys))]
    public async Task GetFeatureFlagResolvesFalseWithoutAFlagsRequest(string? groupKey)
    {
        var container = new TestContainer("fake-personal-api-key");
        // Served verbatim: the typed overload cannot serialize a boolean flag dependency value.
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(UnusableGroupKeys.Definitions, "\"definitions\"");
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(FlagsResponse);
        await using var client = container.Activate<PostHogClient>();

#pragma warning disable CS0618
        foreach (var onlyEvaluateLocally in new[] { true, false })
        {
            foreach (var flagKey in new[] { "org-flag", "dependent-flag" })
            {
                var flag = await client.GetFeatureFlagAsync(
                    flagKey,
                    "user",
                    new FeatureFlagOptions
                    {
                        Groups = UnusableGroupKeys.Organization(groupKey),
                        OnlyEvaluateLocally = onlyEvaluateLocally,
                    },
                    CancellationToken.None);

                Assert.False(flag?.IsEnabled);
            }
        }
#pragma warning restore CS0618

        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Theory]
    [MemberData(nameof(UnusableGroupKeys.All), MemberType = typeof(UnusableGroupKeys))]
    public async Task BulkEvaluationResolvesFalseWithoutAFlagsRequest(string? groupKey)
    {
        var container = new TestContainer("fake-personal-api-key");
        // Served verbatim: the typed overload cannot serialize a boolean flag dependency value.
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(UnusableGroupKeys.Definitions, "\"definitions\"");
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(FlagsResponse);
        await using var client = container.Activate<PostHogClient>();
        var options = new AllFeatureFlagsOptions
        {
            Groups = UnusableGroupKeys.Organization(groupKey),
            PersonProperties = new() { ["plan"] = "free" },
        };

        var snapshot = await client.EvaluateFlagsAsync("user", options, CancellationToken.None);
#pragma warning disable CS0618
        var allFlags = await client.GetAllFeatureFlagsAsync("user", options, CancellationToken.None);
#pragma warning restore CS0618

        foreach (var flagKey in new[] { "org-flag", "mixed-flag", "dependent-flag" })
        {
            Assert.False(snapshot.IsEnabled(flagKey));
            Assert.False(allFlags[flagKey].IsEnabled);
        }
        Assert.Empty(snapshot.UnresolvedFlags);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Theory]
    [MemberData(nameof(UnusableGroupKeys.All), MemberType = typeof(UnusableGroupKeys))]
    public async Task CaptureWithFlagsDoesNotThrow(string? groupKey)
    {
        var container = new TestContainer("fake-personal-api-key");
        // Served verbatim: the typed overload cannot serialize a boolean flag dependency value.
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(UnusableGroupKeys.Definitions, "\"definitions\"");
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(FlagsResponse);
        var batchHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        await using var client = container.Activate<PostHogClient>();
        var groups = UnusableGroupKeys.Organization(groupKey);

        var snapshot = await client.EvaluateFlagsAsync(
            "user",
            new AllFeatureFlagsOptions { Groups = groups, PersonProperties = new() { ["plan"] = "free" } },
            CancellationToken.None);
        Assert.False(snapshot.IsEnabled("org-flag"));
        Assert.True(client.Capture("user", "with-snapshot", properties: null, groups, flags: snapshot));
#pragma warning disable CS0618
        Assert.True(client.Capture("user", "with-send-feature-flags", properties: null, groups, sendFeatureFlags: true));
        Assert.False(await client.IsFeatureEnabledAsync(
            "org-flag", "user", new FeatureFlagOptions { Groups = groups, SendFeatureFlagEvents = true }, CancellationToken.None));
#pragma warning restore CS0618
        await client.FlushAsync();

        var events = JsonDocument.Parse(batchHandler.GetReceivedRequestBody(indented: false)).RootElement.GetProperty("batch");
        Assert.Contains(events.EnumerateArray(), e => e.GetProperty("event").GetString() == "with-snapshot");
        Assert.Contains(events.EnumerateArray(), e => e.GetProperty("event").GetString() == "with-send-feature-flags");
        Assert.Empty(flagsHandler.ReceivedRequests);
    }
}
