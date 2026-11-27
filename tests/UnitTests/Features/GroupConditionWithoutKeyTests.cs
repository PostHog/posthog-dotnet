using System.Text.Json;
using PostHog;
using PostHog.Api;
using PostHog.Exceptions;
using PostHog.Features;
using PostHog.Json;
using UnitTests.Fakes;

namespace GroupConditionWithoutKeyTests;

// Like the flags service, each condition uses its own aggregation: its aggregation_group_type_index when the key is
// present (null means person), otherwise the flag-level one. A group condition without a usable key is skipped, and
// the other conditions still decide the flag.
static class CompanyFlags
{
    // No company group, a company group with an empty key, and one with a null key.
    public static TheoryData<string?, bool> MissingKeys => new()
    {
        { null, false },
        { "", true },
        { null, true },
    };

    public static GroupCollection Groups(string? groupKey, bool withGroup) =>
        withGroup ? new() { new Group("company", groupKey!) } : [];

    public static Dictionary<string, object?> Plan(string plan) => new() { ["plan"] = plan };

    public const string Definitions = """
        {"flags": [
            {"key": "company-mixed", "active": true, "filters": {"aggregation_group_type_index": 0, "groups": [
                {"aggregation_group_type_index": null, "properties": [{"key": "plan", "type": "person", "value": "pro", "operator": "exact"}], "rollout_percentage": 100}
            ]}},
            {"key": "person-dependent", "active": true, "filters": {"groups": [{"properties": [
                {"key": "company-mixed", "type": "flag", "value": true, "operator": "flag_evaluates_to", "dependency_chain": ["company-mixed"]}
            ], "rollout_percentage": 100}]}},
            {"key": "company-then-person", "active": true, "filters": {"aggregation_group_type_index": 0, "groups": [
                {"properties": [], "rollout_percentage": 100},
                {"aggregation_group_type_index": null, "properties": [{"key": "plan", "type": "person", "value": "pro", "operator": "exact"}], "rollout_percentage": 100}
            ]}},
            {"key": "company-only", "active": true, "filters": {"aggregation_group_type_index": 0, "groups": [
                {"properties": [], "rollout_percentage": 100},
                {"aggregation_group_type_index": 0, "properties": [], "rollout_percentage": 100}
            ]}},
            {"key": "company-holdout", "active": true, "filters": {"aggregation_group_type_index": 0,
                "holdout": {"id": 7, "exclusion_percentage": 50},
                "groups": [{"properties": [], "rollout_percentage": 100}]}},
            {"key": "company-holdout-zero", "active": true, "filters": {"aggregation_group_type_index": 0,
                "holdout": {"id": 8, "exclusion_percentage": 0},
                "groups": [{"properties": [], "rollout_percentage": 100}]}},
            {"key": "person-holdout", "active": true, "filters": {
                "holdout": {"id": 9, "exclusion_percentage": 50},
                "groups": [{"properties": [], "rollout_percentage": 100}]}},
            {"key": "person-holdout-zero", "active": true, "filters": {
                "holdout": {"id": 10, "exclusion_percentage": 0},
                "groups": [{"properties": [], "rollout_percentage": 100}]}},
            {"key": "holdout-dependent", "active": true, "filters": {"groups": [{"properties": [
                {"key": "person-holdout", "type": "flag", "value": true, "operator": "flag_evaluates_to", "dependency_chain": ["person-holdout"]}
            ], "rollout_percentage": 100}]}}
        ],
         "group_type_mapping": {"0": "company"}}
        """;

    // The /flags response disagrees with every expected local result, so a request would show in the result.
    public const string FlagsResponse = """
        {"featureFlags": {"company-mixed": "remote", "person-dependent": "remote", "company-then-person": "remote",
         "company-only": "remote", "company-holdout": "remote", "company-holdout-zero": "remote",
         "person-holdout": "remote", "person-holdout-zero": "remote", "holdout-dependent": "remote"}}
        """;

    public static LocalEvaluator CreateEvaluator() =>
        new(JsonSerializer.Deserialize<LocalEvaluationApiResult>(Definitions, JsonSerializerHelper.Options)!);
}

public class TheLocalEvaluator
{
    [Theory]
    [MemberData(nameof(CompanyFlags.MissingKeys), MemberType = typeof(CompanyFlags))]
    public void PersonConditionOfAGroupFlagDecidesWithoutAGroupKey(string? groupKey, bool withGroup)
    {
        var evaluator = CompanyFlags.CreateEvaluator();
        var groups = CompanyFlags.Groups(groupKey, withGroup);

        Assert.True(evaluator.EvaluateFeatureFlag("company-mixed", "user", groups, CompanyFlags.Plan("pro")).Value);
        Assert.False(evaluator.EvaluateFeatureFlag("company-mixed", "user", groups, CompanyFlags.Plan("free")).Value);
    }

    [Theory]
    [MemberData(nameof(CompanyFlags.MissingKeys), MemberType = typeof(CompanyFlags))]
    public void DependencyOnAGroupFlagWithAPersonConditionResolvesFromThatCondition(string? groupKey, bool withGroup)
    {
        var evaluator = CompanyFlags.CreateEvaluator();
        var groups = CompanyFlags.Groups(groupKey, withGroup);

        Assert.True(evaluator.EvaluateFeatureFlag("person-dependent", "user", groups, CompanyFlags.Plan("pro")).Value);
        Assert.False(evaluator.EvaluateFeatureFlag("person-dependent", "user", groups, CompanyFlags.Plan("free")).Value);
    }

    [Theory]
    [MemberData(nameof(CompanyFlags.MissingKeys), MemberType = typeof(CompanyFlags))]
    public void SkippedGroupConditionLeavesTheLaterPersonConditionToDecide(string? groupKey, bool withGroup)
    {
        var evaluator = CompanyFlags.CreateEvaluator();
        var groups = CompanyFlags.Groups(groupKey, withGroup);

        Assert.True(evaluator.EvaluateFeatureFlag("company-then-person", "user", groups, CompanyFlags.Plan("pro")).Value);
        Assert.False(evaluator.EvaluateFeatureFlag("company-then-person", "user", groups, CompanyFlags.Plan("free")).Value);
    }

    [Theory]
    [MemberData(nameof(CompanyFlags.MissingKeys), MemberType = typeof(CompanyFlags))]
    public void GroupFlagWithOnlyGroupConditionsIsFalseWithoutAGroupKey(string? groupKey, bool withGroup)
    {
        var evaluator = CompanyFlags.CreateEvaluator();
        var groups = CompanyFlags.Groups(groupKey, withGroup);

        Assert.False(evaluator.EvaluateFeatureFlag("company-only", "user", groups, CompanyFlags.Plan("pro")).Value);
        Assert.False(evaluator.EvaluateFeatureFlag("company-holdout-zero", "user", groups).Value);
    }

    [Theory]
    [MemberData(nameof(CompanyFlags.MissingKeys), MemberType = typeof(CompanyFlags))]
    public void GroupFlagWithAHoldoutIsMissingContextWithoutAGroupKey(string? groupKey, bool withGroup)
    {
        var evaluator = CompanyFlags.CreateEvaluator();
        var groups = CompanyFlags.Groups(groupKey, withGroup);

        var exception = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("company-holdout", "user", groups));
        Assert.Equal(UnresolvedFlagReason.MissingContext, exception.Reason);
    }

    // The SDK does not evaluate holdouts, so only the flags service can answer for a flag with one.
    [Theory]
    [InlineData("company-holdout")]
    [InlineData("person-holdout")]
    public void FlagWithAHoldoutIsUnsupportedDefinitionWithAUsableKey(string flagKey)
    {
        var evaluator = CompanyFlags.CreateEvaluator();

        var exception = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag(flagKey, "user", CompanyFlags.Groups("acme", withGroup: true)));
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, exception.Reason);
    }

    [Fact]
    public void PersonFlagWithAHoldoutIsUnsupportedDefinitionWithoutGroups()
    {
        var evaluator = CompanyFlags.CreateEvaluator();

        var exception = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("person-holdout", "user"));
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, exception.Reason);
    }

    [Fact]
    public void DependencyOnAFlagWithAHoldoutIsUnresolvedDependency()
    {
        var evaluator = CompanyFlags.CreateEvaluator();

        var exception = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("holdout-dependent", "user"));
        Assert.Equal(UnresolvedFlagReason.UnresolvedDependency, exception.Reason);
    }

    [Fact]
    public void FlagWithAZeroHoldoutIsEvaluatedLocally()
    {
        var evaluator = CompanyFlags.CreateEvaluator();

        Assert.True(evaluator.EvaluateFeatureFlag("company-holdout-zero", "user", CompanyFlags.Groups("acme", withGroup: true)).Value);
        Assert.True(evaluator.EvaluateFeatureFlag("person-holdout-zero", "user").Value);
    }

    [Fact]
    public void LoadedDefinitionOfAFlagWithAHoldoutIsUnsupportedDefinition()
    {
        var evaluator = CompanyFlags.CreateEvaluator();
        var flags = evaluator.LocalEvaluationApiResult.Flags.ToDictionary(flag => flag.Key);

        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, evaluator.GetDefinitionUnresolvedReason(flags["company-holdout"]));
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, evaluator.GetDefinitionUnresolvedReason(flags["person-holdout"]));
        Assert.Null(evaluator.GetDefinitionUnresolvedReason(flags["company-holdout-zero"]));
        Assert.Null(evaluator.GetDefinitionUnresolvedReason(flags["person-holdout-zero"]));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"7\"")]
    [InlineData("{\"id\": 7}")]
    [InlineData("{\"id\": 7, \"exclusion_percentage\": \"50\"}")]
    [InlineData("{\"id\": 7, \"exclusion_percentage\": null}")]
    public void MalformedHoldoutCountsAsNoHoldout(string holdoutJson)
    {
        var evaluator = new LocalEvaluator(JsonSerializer.Deserialize<LocalEvaluationApiResult>($$$"""
            {"flags": [{"key": "company-holdout", "active": true, "filters": {"aggregation_group_type_index": 0,
                "holdout": {{{holdoutJson}}},
                "groups": [{"properties": [], "rollout_percentage": 100}]}},
                {"key": "person-holdout", "active": true, "filters": {
                "holdout": {{{holdoutJson}}},
                "groups": [{"properties": [], "rollout_percentage": 100}]}}],
             "group_type_mapping": {"0": "company"}}
            """, JsonSerializerHelper.Options)!);

        Assert.False(evaluator.EvaluateFeatureFlag("company-holdout", "user").Value);
        Assert.True(evaluator.EvaluateFeatureFlag("company-holdout", "user", CompanyFlags.Groups("acme", withGroup: true)).Value);
        Assert.True(evaluator.EvaluateFeatureFlag("person-holdout", "user").Value);
        Assert.Null(evaluator.GetDefinitionUnresolvedReason(evaluator.LocalEvaluationApiResult.Flags[1]));
    }
}

public class ThePostHogClient
{
    static async Task<(PostHogClient Client, FakeHttpMessageHandler.RequestHandler FlagsHandler)> CreateClientAsync(TestContainer container)
    {
        // Served verbatim: the typed overload cannot serialize a boolean flag dependency value.
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(CompanyFlags.Definitions, "\"definitions\"");
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(CompanyFlags.FlagsResponse);
        var client = container.Activate<PostHogClient>();
        await Task.CompletedTask;
        return (client, flagsHandler);
    }

    [Theory]
    [MemberData(nameof(CompanyFlags.MissingKeys), MemberType = typeof(CompanyFlags))]
    public async Task GetFeatureFlagResolvesPersonConditionsLocally(string? groupKey, bool withGroup)
    {
        var container = new TestContainer("fake-personal-api-key");
        var (client, flagsHandler) = await CreateClientAsync(container);
        await using var _ = client;

#pragma warning disable CS0618
        foreach (var (flagKey, plan, expected) in new[]
                 {
                     ("company-mixed", "pro", true), ("company-mixed", "free", false),
                     ("person-dependent", "pro", true), ("person-dependent", "free", false),
                     ("company-then-person", "pro", true), ("company-then-person", "free", false),
                     ("company-only", "pro", false), ("company-holdout-zero", "pro", false),
                 })
        {
            var flag = await client.GetFeatureFlagAsync(
                flagKey,
                "user",
                new FeatureFlagOptions
                {
                    Groups = CompanyFlags.Groups(groupKey, withGroup),
                    PersonProperties = CompanyFlags.Plan(plan),
                },
                CancellationToken.None);

            Assert.Equal(expected, flag?.IsEnabled);
        }
#pragma warning restore CS0618

        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Theory]
    [MemberData(nameof(CompanyFlags.MissingKeys), MemberType = typeof(CompanyFlags))]
    public async Task EvaluateFlagsResolvesPersonConditionsLocally(string? groupKey, bool withGroup)
    {
        var container = new TestContainer("fake-personal-api-key");
        var (client, flagsHandler) = await CreateClientAsync(container);
        await using var _ = client;

        var snapshot = await client.EvaluateFlagsAsync(
            "user",
            new AllFeatureFlagsOptions
            {
                Groups = CompanyFlags.Groups(groupKey, withGroup),
                PersonProperties = CompanyFlags.Plan("pro"),
                FlagKeysToEvaluate = ["company-mixed", "person-dependent", "company-then-person", "company-only", "company-holdout-zero"],
            },
            CancellationToken.None);

        Assert.True(snapshot.IsEnabled("company-mixed"));
        Assert.True(snapshot.IsEnabled("person-dependent"));
        Assert.True(snapshot.IsEnabled("company-then-person"));
        Assert.False(snapshot.IsEnabled("company-only"));
        Assert.False(snapshot.IsEnabled("company-holdout-zero"));
        Assert.Empty(snapshot.UnresolvedFlags);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Theory]
    [MemberData(nameof(CompanyFlags.MissingKeys), MemberType = typeof(CompanyFlags))]
    public async Task GroupFlagWithAHoldoutFallsBackToFlagsWithoutAGroupKey(string? groupKey, bool withGroup)
    {
        var container = new TestContainer("fake-personal-api-key");
        var (client, flagsHandler) = await CreateClientAsync(container);
        await using var _ = client;

#pragma warning disable CS0618
        var flag = await client.GetFeatureFlagAsync(
            "company-holdout",
            "user",
            new FeatureFlagOptions { Groups = CompanyFlags.Groups(groupKey, withGroup) },
            CancellationToken.None);
#pragma warning restore CS0618

        Assert.Equal("remote", flag?.VariantKey);
        Assert.Single(flagsHandler.ReceivedRequests);
    }

    [Theory]
    [MemberData(nameof(CompanyFlags.MissingKeys), MemberType = typeof(CompanyFlags))]
    public async Task GroupFlagWithAHoldoutIsUnresolvedLocallyWithoutAGroupKey(string? groupKey, bool withGroup)
    {
        var container = new TestContainer("fake-personal-api-key");
        var (client, flagsHandler) = await CreateClientAsync(container);
        await using var _ = client;

        var snapshot = await client.EvaluateFlagsAsync(
            "user",
            new AllFeatureFlagsOptions
            {
                Groups = CompanyFlags.Groups(groupKey, withGroup),
                FlagKeysToEvaluate = ["company-holdout"],
                OnlyEvaluateLocally = true,
            },
            CancellationToken.None);

        Assert.Null(snapshot.GetFlag("company-holdout"));
        Assert.Equal(UnresolvedFlagReason.MissingContext, snapshot.UnresolvedFlags["company-holdout"]);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Theory]
    [InlineData("company-holdout")]
    [InlineData("person-holdout")]
    [InlineData("holdout-dependent")]
    public async Task FlagWithAHoldoutFallsBackToFlagsWithAUsableKey(string flagKey)
    {
        var container = new TestContainer("fake-personal-api-key");
        var (client, flagsHandler) = await CreateClientAsync(container);
        await using var _ = client;

#pragma warning disable CS0618
        var flag = await client.GetFeatureFlagAsync(
            flagKey,
            "user",
            new FeatureFlagOptions { Groups = CompanyFlags.Groups("acme", withGroup: true) },
            CancellationToken.None);
#pragma warning restore CS0618

        Assert.Equal("remote", flag?.VariantKey);
        Assert.Single(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task FlagWithAHoldoutIsUnresolvedLocallyWithAUsableKey()
    {
        var container = new TestContainer("fake-personal-api-key");
        var (client, flagsHandler) = await CreateClientAsync(container);
        await using var _ = client;

        var snapshot = await client.EvaluateFlagsAsync(
            "user",
            new AllFeatureFlagsOptions
            {
                Groups = CompanyFlags.Groups("acme", withGroup: true),
                FlagKeysToEvaluate = ["company-holdout", "person-holdout", "holdout-dependent", "company-holdout-zero", "person-holdout-zero"],
                OnlyEvaluateLocally = true,
            },
            CancellationToken.None);

        Assert.Null(snapshot.GetFlag("company-holdout"));
        Assert.Null(snapshot.GetFlag("person-holdout"));
        Assert.Null(snapshot.GetFlag("holdout-dependent"));
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, snapshot.UnresolvedFlags["company-holdout"]);
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, snapshot.UnresolvedFlags["person-holdout"]);
        Assert.Equal(UnresolvedFlagReason.UnresolvedDependency, snapshot.UnresolvedFlags["holdout-dependent"]);
        Assert.True(snapshot.IsEnabled("company-holdout-zero"));
        Assert.True(snapshot.IsEnabled("person-holdout-zero"));
        Assert.Equal(3, snapshot.UnresolvedFlags.Count);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }
}
