using System.Text.Json;
using NSubstitute;
using PostHog;
using PostHog.Api;
using PostHog.Exceptions;
using PostHog.Features;
using PostHog.Json;
using UnitTests.Fakes;

namespace KnownAbsentPropertyKeysTests;

public class TheLocalEvaluator
{
    static async Task<LocalEvaluator> CreateEvaluatorAsync(string definitionsJson)
    {
        var definitions = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(
            definitionsJson);
        return new LocalEvaluator(definitions!);
    }

    static string PlanFlag(string operatorName, string valueJson = "\"pro\"") => $$$"""
        {"flags": [{"key": "plan-flag", "active": true, "filters": {"groups": [{"properties": [
            {"key": "plan", "type": "person", "value": {{{valueJson}}}, "operator": "{{{operatorName}}}"}
        ], "rollout_percentage": 100}]}}]}
        """;

    static KnownAbsentPropertyKeys? Person(params string[] keys)
        => KnownAbsentPropertyKeys.Create(new AllFeatureFlagsOptions { PersonPropertyKeysKnownAbsent = keys });

    static KnownAbsentPropertyKeys? Group(string groupType, params string[] keys)
        => KnownAbsentPropertyKeys.Create(new AllFeatureFlagsOptions
        {
            GroupPropertyKeysKnownAbsent = new Dictionary<string, IReadOnlyCollection<string>> { [groupType] = keys }
        });

    [Theory]
    [InlineData("is_not_set", "\"pro\"", true)]
    [InlineData("is_set", "\"pro\"", false)]
    [InlineData("exact", "\"pro\"", false)]
    [InlineData("is_not", "\"pro\"", true)]
    [InlineData("icontains", "\"pro\"", false)]
    [InlineData("not_icontains", "\"pro\"", true)]
    [InlineData("starts_with", "\"pro\"", false)]
    [InlineData("not_starts_with", "\"pro\"", true)]
    [InlineData("ends_with", "\"pro\"", false)]
    [InlineData("not_ends_with", "\"pro\"", true)]
    [InlineData("regex", "\"^pro\"", false)]
    [InlineData("not_regex", "\"^pro\"", true)]
    [InlineData("gt", "\"10\"", false)]
    [InlineData("gte", "\"10\"", false)]
    [InlineData("lt", "\"10\"", false)]
    [InlineData("lte", "\"10\"", false)]
    [InlineData("semver_eq", "\"1.2.3\"", false)]
    [InlineData("semver_neq", "\"1.2.3\"", false)]
    [InlineData("semver_gt", "\"1.2.3\"", false)]
    [InlineData("semver_tilde", "\"1.2.3\"", false)]
    [InlineData("semver_caret", "\"1.2.3\"", false)]
    [InlineData("semver_wildcard", "\"1.2.*\"", false)]
    [InlineData("is_date_before", "\"2025-01-01T10:00:00Z\"", false)]
    [InlineData("is_date_after", "\"2025-01-01T10:00:00Z\"", false)]
    public async Task ResolvesEachOperatorForAListedPersonKey(string operatorName, string valueJson, bool expected)
    {
        var evaluator = await CreateEvaluatorAsync(PlanFlag(operatorName, valueJson));

        var result = evaluator.EvaluateFeatureFlag("plan-flag", "user", knownAbsent: Person("plan"));

        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("is_not_set", true)]
    [InlineData("is_set", false)]
    [InlineData("exact", false)]
    [InlineData("is_not", true)]
    public async Task ListedKeyOverridesASuppliedValue(string operatorName, bool expected)
    {
        var evaluator = await CreateEvaluatorAsync(PlanFlag(operatorName));

        var result = evaluator.EvaluateFeatureFlag(
            "plan-flag",
            "user",
            personProperties: new() { ["plan"] = "pro" },
            knownAbsent: Person("plan"));

        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public async Task ListedDistinctIdOverridesTheInjectedValue()
    {
        var evaluator = await CreateEvaluatorAsync("""
            {"flags": [{"key": "plan-flag", "active": true, "filters": {"groups": [{"properties": [
                {"key": "distinct_id", "type": "person", "operator": "is_not_set"}
            ], "rollout_percentage": 100}]}}]}
            """);

        Assert.True(evaluator.EvaluateFeatureFlag("plan-flag", "user", knownAbsent: Person("distinct_id")).Value);
    }

    [Fact]
    public async Task OmittedKeyThatIsNotListedStaysInconclusive()
    {
        var evaluator = await CreateEvaluatorAsync(PlanFlag("is_not_set"));

        var exception = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("plan-flag", "user", knownAbsent: Person("region")));

        Assert.Equal(UnresolvedFlagReason.MissingContext, exception.Reason);
    }

    [Fact]
    public async Task PersonMetadataKeyIsIgnored()
    {
        const string key = "__posthog_person_metadata__created_at";
        var evaluator = await CreateEvaluatorAsync($$$"""
            {"flags": [{"key": "plan-flag", "active": true, "filters": {"groups": [{"properties": [
                {"key": "{{{key}}}", "type": "person", "operator": "is_set"}
            ], "rollout_percentage": 100}]}}]}
            """);

        var result = evaluator.EvaluateFeatureFlag(
            "plan-flag",
            "user",
            personProperties: new() { [key] = "2024-01-01T00:00:00Z" },
            knownAbsent: Person(key));

        Assert.True(result.Value);
        Assert.Null(Person(key));
    }

    [Theory]
    [InlineData("operator_from_the_future", "\"pro\"")]
    [InlineData("icontains_multi", "[\"pro\"]")]
    [InlineData("not_icontains_multi", "[\"pro\"]")]
    [InlineData("between", "[1, 2]")]
    [InlineData("not_between", "[1, 2]")]
    public async Task UnsupportedOperatorOnAListedKeyStaysInconclusive(string operatorName, string valueJson)
    {
        var evaluator = await CreateEvaluatorAsync(PlanFlag(operatorName, valueJson));

        var exception = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("plan-flag", "user", knownAbsent: Person("plan")));

        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, exception.Reason);
    }

    // A filter whose "value" field is omitted (valueJson is null) or JSON null.
    static string PlanFlagWithoutValue(string operatorName, string? valueJson) => $$$"""
        {"flags": [{"key": "plan-flag", "active": true, "filters": {"groups": [{"properties": [
            {"key": "plan", "type": "person", {{{(valueJson is null ? "" : $"\"value\": {valueJson}, ")}}}"operator": "{{{operatorName}}}"}
        ], "rollout_percentage": 100}]}}]}
        """;

    [Theory]
    [InlineData("is_not", null)]
    [InlineData("is_not", "null")]
    [InlineData("exact", "null")]
    [InlineData("gt", "null")]
    public async Task ValueRequiringOperatorWithoutAValueStaysInconclusive(string operatorName, string? valueJson)
    {
        var evaluator = await CreateEvaluatorAsync(PlanFlagWithoutValue(operatorName, valueJson));

        var listed = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("plan-flag", "user", knownAbsent: Person("plan")));
        var present = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("plan-flag", "user", personProperties: new() { ["plan"] = "pro" }));

        // A listed key reports the same reason as a present key with the same filter.
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, listed.Reason);
        Assert.Equal(present.Reason, listed.Reason);
    }

    [Theory]
    [InlineData("is_not_set", true)]
    [InlineData("is_set", false)]
    public async Task SetOperatorWithANullValueResolvesForAListedKey(string operatorName, bool expected)
    {
        var evaluator = await CreateEvaluatorAsync(PlanFlagWithoutValue(operatorName, "null"));

        var result = evaluator.EvaluateFeatureFlag("plan-flag", "user", knownAbsent: Person("plan"));

        Assert.Equal(expected, result.Value);
    }

    // The flags service decides a listed key before it reads the condition value, so a value the SDK could not
    // parse or compile does not change the result.
    [Theory]
    [InlineData("not_regex", "\"[\"", true)]
    [InlineData("regex", "\"[\"", false)]
    [InlineData("not_regex", "\"\\\\p{L}\"", true)]
    [InlineData("regex", "\"\\\\p{L}\"", false)]
    [InlineData("not_regex", "\"(?=pro)\"", true)]
    [InlineData("regex", "\"(?=pro)\"", false)]
    [InlineData("gt", "\"not-a-number\"", false)]
    [InlineData("gte", "\"not-a-number\"", false)]
    [InlineData("lt", "\"not-a-number\"", false)]
    [InlineData("lte", "\"not-a-number\"", false)]
    [InlineData("semver_gt", "\"not-a-version\"", false)]
    [InlineData("semver_neq", "\"not-a-version\"", false)]
    [InlineData("semver_wildcard", "\"not-a-version\"", false)]
    [InlineData("is_date_before", "\"not-a-date\"", false)]
    [InlineData("is_date_after", "\"not-a-date\"", false)]
    public async Task ListedKeyResolvesWithoutReadingTheConditionValue(string operatorName, string valueJson, bool expected)
    {
        var evaluator = await CreateEvaluatorAsync(PlanFlag(operatorName, valueJson));

        var result = evaluator.EvaluateFeatureFlag("plan-flag", "user", knownAbsent: Person("plan"));

        Assert.Equal(expected, result.Value);
    }

    const string OrganizationFlag = """
        {"flags": [{"key": "org-flag", "active": true, "filters": {"aggregation_group_type_index": 0, "groups": [{"properties": [
            {"key": "tier", "type": "group", "group_type_index": 0, "operator": "is_not_set"}
        ], "rollout_percentage": 100}]}}],
         "group_type_mapping": {"0": "organization", "1": "project"}}
        """;

    [Fact]
    public async Task GroupKeyAppliesToItsGroupType()
    {
        var evaluator = await CreateEvaluatorAsync(OrganizationFlag);
        var groups = new GroupCollection { new Group("organization", "org-1", new Dictionary<string, object?> { ["tier"] = "gold" }) };

        var result = evaluator.EvaluateFeatureFlag("org-flag", "user", groups, knownAbsent: Group("organization", "tier"));

        Assert.True(result.Value);
    }

    [Fact]
    public async Task GroupKeyListedForAnotherGroupTypeStaysInconclusive()
    {
        var evaluator = await CreateEvaluatorAsync(OrganizationFlag);
        var groups = new GroupCollection { new Group("organization", "org-1") };

        var exception = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("org-flag", "user", groups, knownAbsent: Group("project", "tier")));

        Assert.Equal(UnresolvedFlagReason.MissingContext, exception.Reason);
    }

    [Fact]
    public async Task PersonKeyDoesNotApplyToGroupProperties()
    {
        var evaluator = await CreateEvaluatorAsync(OrganizationFlag);
        var groups = new GroupCollection { new Group("organization", "org-1") };

        Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("org-flag", "user", groups, knownAbsent: Person("tier")));
    }

    [Fact]
    public async Task MixedConditionUsesTheConditionGroupType()
    {
        var evaluator = await CreateEvaluatorAsync("""
            {"flags": [{"key": "mixed-flag", "active": true, "filters": {"groups": [
                {"aggregation_group_type_index": 0, "properties": [
                    {"key": "tier", "type": "group", "group_type_index": 0, "operator": "is_not_set"}
                ], "rollout_percentage": 100}
            ]}}],
             "group_type_mapping": {"0": "organization"}}
            """);
        var groups = new GroupCollection { new Group("organization", "org-1") };

        Assert.True(evaluator.EvaluateFeatureFlag("mixed-flag", "user", groups, knownAbsent: Group("organization", "tier")).Value);
        Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("mixed-flag", "user", groups, knownAbsent: Person("tier")));
    }

    static string CohortFlag(string operatorName, bool negation, string valueJson = "\"pro\"") => $$$"""
        {"flags": [{"key": "cohort-flag", "active": true, "filters": {"groups": [{"properties": [
            {"key": "id", "type": "cohort", "value": 7}
        ], "rollout_percentage": 100}]}}],
         "cohorts": {"7": {"type": "AND", "values": [{"type": "AND", "values": [
            {"key": "plan", "type": "person", "value": {{{valueJson}}}, "operator": "{{{operatorName}}}", "negation": {{{(negation ? "true" : "false")}}}}
         ]}]} } }
        """;

    [Theory]
    [InlineData("is_not_set", false, true)]
    [InlineData("is_not_set", true, false)]
    [InlineData("exact", false, false)]
    [InlineData("exact", true, true)]
    [InlineData("not_icontains", false, true)]
    [InlineData("not_icontains", true, false)]
    public async Task CohortLeafAppliesNegationAfterTheKnownAbsentResult(string operatorName, bool negation, bool expected)
    {
        var evaluator = await CreateEvaluatorAsync(CohortFlag(operatorName, negation));

        var result = evaluator.EvaluateFeatureFlag(
            "cohort-flag",
            "user",
            personProperties: new() { ["plan"] = "pro" },
            knownAbsent: Person("plan"));

        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("not_regex", "\"[\"", false, true)]
    [InlineData("not_regex", "\"[\"", true, false)]
    [InlineData("regex", "\"\\\\p{L}\"", false, false)]
    [InlineData("gt", "\"not-a-number\"", true, true)]
    [InlineData("semver_gt", "\"not-a-version\"", false, false)]
    [InlineData("is_date_after", "\"not-a-date\"", false, false)]
    public async Task CohortLeafOnAListedKeyResolvesWithoutReadingTheConditionValue(
        string operatorName,
        string valueJson,
        bool negation,
        bool expected)
    {
        var evaluator = await CreateEvaluatorAsync(CohortFlag(operatorName, negation, valueJson));

        var result = evaluator.EvaluateFeatureFlag("cohort-flag", "user", knownAbsent: Person("plan"));

        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedCohortLeafOperatorOnAListedKeyStaysInconclusive(bool negation)
    {
        var evaluator = await CreateEvaluatorAsync(CohortFlag("not_between", negation, "[1, 2]"));

        var exception = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("cohort-flag", "user", knownAbsent: Person("plan")));

        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, exception.Reason);
    }

    [Fact]
    public async Task StaticCohortStillRequiresServerEvaluation()
    {
        var evaluator = await CreateEvaluatorAsync("""
            {"flags": [{"key": "cohort-flag", "active": true, "filters": {"groups": [{"properties": [
                {"key": "id", "type": "cohort", "value": 99}
            ], "rollout_percentage": 100}]}}]}
            """);

        Assert.Throws<RequiresServerEvaluationException>(
            () => evaluator.EvaluateFeatureFlag("cohort-flag", "user", knownAbsent: Person("plan")));
    }

    [Fact]
    public async Task FlagDependencyEvaluatesWithTheSameListing()
    {
        var evaluator = await CreateEvaluatorAsync("""
            {"flags": [
                {"key": "base-flag", "active": true, "filters": {"groups": [{"properties": [
                    {"key": "plan", "type": "person", "operator": "is_not_set"}
                ], "rollout_percentage": 100}]}},
                {"key": "dependent-flag", "active": true, "filters": {"groups": [{"properties": [
                    {"key": "base-flag", "type": "flag", "value": true, "operator": "flag_evaluates_to", "dependency_chain": ["base-flag"]}
                ], "rollout_percentage": 100}]}}
            ]}
            """);

        Assert.True(evaluator.EvaluateFeatureFlag("dependent-flag", "user", knownAbsent: Person("plan")).Value);
        var exception = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("dependent-flag", "user"));
        Assert.Equal(UnresolvedFlagReason.UnresolvedDependency, exception.Reason);
    }

    [Fact]
    public async Task PersonKeyDoesNotApplyToAGroupFilterInAPersonFlag()
    {
        var evaluator = await CreateEvaluatorAsync("""
            {"flags": [{"key": "person-flag", "active": true, "filters": {"groups": [{"properties": [
                {"key": "tier", "type": "group", "group_type_index": 0, "operator": "is_not_set"}
            ], "rollout_percentage": 100}]}}],
             "group_type_mapping": {"0": "organization"}}
            """);

        var exception = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("person-flag", "user", knownAbsent: Person("tier")));

        Assert.Equal(UnresolvedFlagReason.MissingContext, exception.Reason);
    }

    [Fact]
    public async Task GroupKeyDoesNotApplyToAPersonFilterOrCohortLeafInAGroupFlag()
    {
        var evaluator = await CreateEvaluatorAsync("""
            {"flags": [
                {"key": "org-flag", "active": true, "filters": {"aggregation_group_type_index": 0, "groups": [{"properties": [
                    {"key": "tier", "type": "person", "operator": "is_not_set"}
                ], "rollout_percentage": 100}]}},
                {"key": "org-cohort-flag", "active": true, "filters": {"aggregation_group_type_index": 0, "groups": [{"properties": [
                    {"key": "id", "type": "cohort", "value": 7}
                ], "rollout_percentage": 100}]}}
            ],
             "cohorts": {"7": {"type": "AND", "values": [{"type": "AND", "values": [
                {"key": "tier", "type": "person", "operator": "is_not_set"}
             ]}]}},
             "group_type_mapping": {"0": "organization"}}
            """);
        var groups = new GroupCollection { new Group("organization", "org-1") };

        var flagException = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("org-flag", "user", groups, knownAbsent: Group("organization", "tier")));
        var cohortException = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("org-cohort-flag", "user", groups, knownAbsent: Group("organization", "tier")));

        Assert.Equal(UnresolvedFlagReason.MissingContext, flagException.Reason);
        Assert.Equal(UnresolvedFlagReason.MissingContext, cohortException.Reason);
    }

    [Fact]
    public async Task GroupKeyDoesNotApplyToAFilterOfAnotherGroupType()
    {
        var evaluator = await CreateEvaluatorAsync("""
            {"flags": [{"key": "org-flag", "active": true, "filters": {"aggregation_group_type_index": 0, "groups": [{"properties": [
                {"key": "tier", "type": "group", "group_type_index": 1, "operator": "is_not_set"}
            ], "rollout_percentage": 100}]}}],
             "group_type_mapping": {"0": "organization", "1": "project"}}
            """);
        var groups = new GroupCollection { new Group("organization", "org-1") };

        var exception = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("org-flag", "user", groups, knownAbsent: Group("organization", "tier")));

        Assert.Equal(UnresolvedFlagReason.MissingContext, exception.Reason);
    }

    // org-flag aggregates by organization and depends on base-flag, which aggregates by person.
    const string GroupDependsOnPersonDefinitions = """
        {"flags": [
            {"key": "base-flag", "active": true, "filters": {"groups": [{"properties": [
                {"key": "tier", "type": "person", "operator": "is_not_set"}
            ], "rollout_percentage": 100}]}},
            {"key": "org-flag", "active": true, "filters": {"aggregation_group_type_index": 0, "groups": [{"properties": [
                {"key": "base-flag", "type": "flag", "value": true, "operator": "flag_evaluates_to", "dependency_chain": ["base-flag"]}
            ], "rollout_percentage": 100}]}}
        ],
         "group_type_mapping": {"0": "organization"}}
        """;

    [Fact]
    public async Task GroupKeyDoesNotLeakIntoAPersonFlagDependency()
    {
        var evaluator = await CreateEvaluatorAsync(GroupDependsOnPersonDefinitions);
        var groups = new GroupCollection { new Group("organization", "org-1") };

        var exception = Assert.Throws<InconclusiveMatchException>(
            () => evaluator.EvaluateFeatureFlag("org-flag", "user", groups, knownAbsent: Group("organization", "tier")));

        Assert.Equal(UnresolvedFlagReason.UnresolvedDependency, exception.Reason);
    }

    [Fact]
    public async Task PersonFlagDependencyOfAGroupFlagKeepsThePersonKeys()
    {
        var evaluator = await CreateEvaluatorAsync(GroupDependsOnPersonDefinitions);
        var groups = new GroupCollection { new Group("organization", "org-1") };

        Assert.True(evaluator.EvaluateFeatureFlag("org-flag", "user", groups, knownAbsent: Person("tier")).Value);
    }

    [Fact]
    public async Task EvaluateAllFlagsResolvesListedKeysWithoutFallback()
    {
        var evaluator = await CreateEvaluatorAsync(PlanFlag("is_not_set"));

        var (results, fallbackToRemote, unresolved) = evaluator.EvaluateAllFlags("user", knownAbsent: Person("plan"));

        Assert.True(results["plan-flag"].IsEnabled);
        Assert.False(fallbackToRemote);
        Assert.Empty(unresolved);
    }
}

public class ThePostHogClient
{
    const string Definitions = """
        {"flags": [
            {"key": "plan-flag", "active": true, "filters": {"groups": [{"properties": [
                {"key": "plan", "type": "person", "operator": "is_not_set"}
            ], "rollout_percentage": 100}]}},
            {"key": "region-flag", "active": true, "filters": {"groups": [{"properties": [
                {"key": "region", "type": "person", "value": "eu", "operator": "exact"}
            ], "rollout_percentage": 100}]}}
        ]}
        """;

    [Fact]
    public async Task GetFeatureFlagResolvesLocallyWithoutFlagsRequest()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(Definitions);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"plan-flag": false}}""");
        await using var client = container.Activate<PostHogClient>();

#pragma warning disable CS0618
        var flag = await client.GetFeatureFlagAsync(
            "plan-flag",
            "user",
            new FeatureFlagOptions
            {
                PersonProperties = new() { ["plan"] = "pro" },
                PersonPropertyKeysKnownAbsent = ["plan"],
                SendFeatureFlagEvents = false
            },
            CancellationToken.None);
#pragma warning restore CS0618

        Assert.True(flag?.IsEnabled);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    static string PlanFlagDefinitions(string operatorName, string valueJson) => $$$"""
        {"flags": [{"key": "plan-flag", "active": true, "filters": {"groups": [{"properties": [
            {"key": "plan", "type": "person", "value": {{{valueJson}}}, "operator": "{{{operatorName}}}"}
        ], "rollout_percentage": 100}]}}]}
        """;

    // The /flags response disagrees with the expected local result, so a request would show in the result.
    const string FlagsResponseEnabled = """{"featureFlags": {"plan-flag": true}}""";
    const string FlagsResponseDisabled = """{"featureFlags": {"plan-flag": false}}""";

    public static TheoryData<string, string, bool> ConditionValuesNotRead => new()
    {
        { "not_regex", "\"[\"", true },
        { "regex", "\"[\"", false },
        { "not_regex", "\"\\\\p{L}\"", true },
        { "regex", "\"\\\\p{L}\"", false },
        { "gt", "\"not-a-number\"", false },
        { "lte", "\"not-a-number\"", false },
        { "semver_gt", "\"not-a-version\"", false },
        { "is_date_after", "\"not-a-date\"", false },
    };

    [Theory]
    [MemberData(nameof(ConditionValuesNotRead))]
    public async Task ListedKeyResolvesLocallyWithoutReadingTheConditionValue(string operatorName, string valueJson, bool expected)
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(PlanFlagDefinitions(operatorName, valueJson));
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(expected ? FlagsResponseDisabled : FlagsResponseEnabled);
        await using var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user",
            new AllFeatureFlagsOptions { PersonPropertyKeysKnownAbsent = ["plan"], OnlyEvaluateLocally = true },
            CancellationToken.None);

        Assert.Equal(expected, snapshot.IsEnabled("plan-flag"));
        Assert.Empty(snapshot.UnresolvedFlags);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Theory]
    [MemberData(nameof(ConditionValuesNotRead))]
    public async Task ListedKeyResolvesWithoutAFlagsRequestWhenTheConditionValueIsUnreadable(string operatorName, string valueJson, bool expected)
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(PlanFlagDefinitions(operatorName, valueJson));
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(expected ? FlagsResponseDisabled : FlagsResponseEnabled);
        await using var client = container.Activate<PostHogClient>();

#pragma warning disable CS0618
        var flag = await client.GetFeatureFlagAsync(
            "plan-flag",
            "user",
            new FeatureFlagOptions { PersonPropertyKeysKnownAbsent = ["plan"], SendFeatureFlagEvents = false },
            CancellationToken.None);
#pragma warning restore CS0618

        Assert.Equal(expected, flag?.IsEnabled);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Theory]
    [InlineData("icontains_multi", "[\"pro\"]")]
    [InlineData("not_icontains_multi", "[\"pro\"]")]
    [InlineData("between", "[1, 2]")]
    [InlineData("not_between", "[1, 2]")]
    [InlineData("operator_from_the_future", "\"pro\"")]
    public async Task UnsupportedOperatorOnAListedKeyFallsBackToFlags(string operatorName, string valueJson)
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(PlanFlagDefinitions(operatorName, valueJson));
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"plan-flag": true}}""");
        await using var client = container.Activate<PostHogClient>();

        var localOnly = await client.EvaluateFlagsAsync(
            "user",
            new AllFeatureFlagsOptions { PersonPropertyKeysKnownAbsent = ["plan"], OnlyEvaluateLocally = true },
            CancellationToken.None);
#pragma warning disable CS0618
        var flag = await client.GetFeatureFlagAsync(
            "plan-flag",
            "user",
            new FeatureFlagOptions { PersonPropertyKeysKnownAbsent = ["plan"], SendFeatureFlagEvents = false },
            CancellationToken.None);
#pragma warning restore CS0618

        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, localOnly.UnresolvedFlags["plan-flag"]);
        Assert.True(flag?.IsEnabled);
        Assert.Single(flagsHandler.ReceivedRequests);
    }

    // "plan" is listed as known absent; is_not would match from the known-absent table if the missing value were ignored.
    static string IsNotPlanDefinitions(string? valueJson) => $$$"""
        {"flags": [{"key": "plan-flag", "active": true, "filters": {"groups": [{"properties": [
            {"key": "plan", "type": "person", {{{(valueJson is null ? "" : $"\"value\": {valueJson}, ")}}}"operator": "is_not"}
        ], "rollout_percentage": 100}]}}]}
        """;

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    public async Task OmittedOrNullConditionValueOnAListedKeyIsUnresolvedLocally(string? valueJson)
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(IsNotPlanDefinitions(valueJson));
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"plan-flag": false}}""");
        await using var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user",
            new AllFeatureFlagsOptions { PersonPropertyKeysKnownAbsent = ["plan"], OnlyEvaluateLocally = true },
            CancellationToken.None);

        Assert.Null(snapshot.GetFlag("plan-flag"));
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, snapshot.UnresolvedFlags["plan-flag"]);
        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    public async Task OmittedOrNullConditionValueOnAListedKeyFallsBackToFlags(string? valueJson)
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(IsNotPlanDefinitions(valueJson));
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"plan-flag": false}}""");
        await using var client = container.Activate<PostHogClient>();

#pragma warning disable CS0618
        var flag = await client.GetFeatureFlagAsync(
            "plan-flag",
            "user",
            new FeatureFlagOptions { PersonPropertyKeysKnownAbsent = ["plan"], SendFeatureFlagEvents = false },
            CancellationToken.None);
#pragma warning restore CS0618

        Assert.False(flag?.IsEnabled);
        Assert.Single(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task RemoteFallbackSendsTheListedKeysAndSkipsTheCache()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(Definitions);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"region-flag": true}}""");
        var cache = Substitute.For<IFeatureFlagCache>();
        await using var client = container.Activate<PostHogClient>(cache);

        var snapshot = await client.EvaluateFlagsAsync(
            "user",
            new AllFeatureFlagsOptions
            {
                PersonPropertyKeysKnownAbsent = ["plan"],
                GroupPropertyKeysKnownAbsent = new Dictionary<string, IReadOnlyCollection<string>>
                {
                    ["organization"] = ["tier"],
                    ["project"] = []
                }
            },
            CancellationToken.None);

        Assert.True(snapshot.IsEnabled("plan-flag"));
        Assert.True(snapshot.IsEnabled("region-flag"));
        using var body = JsonDocument.Parse(flagsHandler.GetReceivedRequestBody(indented: false));
        Assert.Equal(
            """["plan"]""",
            body.RootElement.GetProperty("person_property_keys_known_absent").GetRawText());
        Assert.Equal(
            """{"organization":["tier"]}""",
            body.RootElement.GetProperty("group_property_keys_known_absent").GetRawText());
        Assert.Empty(cache.ReceivedCalls());
    }

    [Fact]
    public async Task RequestWithoutListedKeysOmitsTheFieldsAndUsesTheCache()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(Definitions);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"region-flag": true}}""");
        using var cache = new MemoryFeatureFlagCache(container.FakeTimeProvider, 100, 0.2);
        await using var client = container.Activate<PostHogClient>(cache);
        var options = new AllFeatureFlagsOptions
        {
            PersonPropertyKeysKnownAbsent = [],
            GroupPropertyKeysKnownAbsent = new Dictionary<string, IReadOnlyCollection<string>> { ["organization"] = [] }
        };

        await client.GetAllFeatureFlagsAsync("user", options, CancellationToken.None);
        await client.GetAllFeatureFlagsAsync("user", options, CancellationToken.None);

        Assert.Single(flagsHandler.ReceivedRequests);
        using var body = JsonDocument.Parse(flagsHandler.GetReceivedRequestBody(indented: false));
        Assert.False(body.RootElement.TryGetProperty("person_property_keys_known_absent", out _));
        Assert.False(body.RootElement.TryGetProperty("group_property_keys_known_absent", out _));
    }

    [Fact]
    public async Task ListedKeysBypassAWarmCache()
    {
        var container = new TestContainer();
        var firstHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"plan-flag": false}}""");
        var secondHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"plan-flag": true}}""");
        using var cache = new MemoryFeatureFlagCache(container.FakeTimeProvider, 100, 0.2);
        await using var client = container.Activate<PostHogClient>(cache);

        var cached = await client.GetAllFeatureFlagsAsync("user", options: null, CancellationToken.None);
        var listed = await client.GetAllFeatureFlagsAsync(
            "user",
            new AllFeatureFlagsOptions { PersonPropertyKeysKnownAbsent = ["plan"] },
            CancellationToken.None);

        Assert.False(cached["plan-flag"].IsEnabled);
        Assert.True(listed["plan-flag"].IsEnabled);
        Assert.Single(firstHandler.ReceivedRequests);
        Assert.Single(secondHandler.ReceivedRequests);
        Assert.Contains("person_property_keys_known_absent", secondHandler.GetReceivedRequestBody(indented: false), StringComparison.Ordinal);
    }

    // created_at is a persons-table field, not a person property, so a person listing never resolves it.
    public static TheoryData<string> PersonMetadataDefinitions()
    {
        var data = new TheoryData<string>();
        foreach (var (operatorName, valueJson) in new[]
                 {
                     ("is_not_set", "null"),
                     ("is_date_before", "\"2100-01-01T00:00:00Z\""),
                     ("is_date_after", "\"2000-01-01T00:00:00Z\"")
                 })
        {
            var filter = $$"""{"key": "created_at", "type": "person_metadata", "value": {{valueJson}}, "operator": "{{operatorName}}"}""";
            data.Add($$$"""
                {"flags": [{"key": "created-flag", "active": true, "filters": {"groups": [{"properties": [
                    {{{filter}}}
                ], "rollout_percentage": 100}]}}]}
                """);
            data.Add($$$"""
                {"flags": [{"key": "created-flag", "active": true, "filters": {"groups": [{"properties": [
                    {"key": "id", "type": "cohort", "value": 7}
                ], "rollout_percentage": 100}]}}],
                 "cohorts": {"7": {"type": "AND", "values": [{"type": "AND", "values": [{{{filter}}}]}]} }}
                """);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(PersonMetadataDefinitions))]
    public async Task PersonMetadataFilterOnAListedKeyFallsBackToFlags(string definitions)
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(definitions, "\"v1\"");
        // Every operator here would match from the known-absent table, so a local result would be true.
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"created-flag": false}}""");
        await using var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user",
            new AllFeatureFlagsOptions { PersonPropertyKeysKnownAbsent = ["created_at"] },
            CancellationToken.None);

        Assert.False(snapshot.IsEnabled("created-flag"));
        Assert.Single(flagsHandler.ReceivedRequests);
    }

    // org-flag aggregates by organization and depends on base-flag, which aggregates by person.
    const string GroupDependsOnPersonDefinitions = """
        {"flags": [
            {"key": "base-flag", "active": true, "filters": {"groups": [{"properties": [
                {"key": "tier", "type": "person", "operator": "is_not_set"}
            ], "rollout_percentage": 100}]}},
            {"key": "org-flag", "active": true, "filters": {"aggregation_group_type_index": 0, "groups": [{"properties": [
                {"key": "base-flag", "type": "flag", "value": true, "operator": "flag_evaluates_to", "dependency_chain": ["base-flag"]}
            ], "rollout_percentage": 100}]}}
        ],
         "group_type_mapping": {"0": "organization"}}
        """;

    [Fact]
    public async Task GroupKeyDoesNotLeakIntoAPersonFlagDependency()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(GroupDependsOnPersonDefinitions, "\"v1\"");
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {"base-flag": false, "org-flag": false}}""");
        await using var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user",
            new AllFeatureFlagsOptions
            {
                Groups = [new Group("organization", "org-1")],
                GroupPropertyKeysKnownAbsent = new Dictionary<string, IReadOnlyCollection<string>> { ["organization"] = ["tier"] }
            },
            CancellationToken.None);

        Assert.False(snapshot.IsEnabled("base-flag"));
        Assert.False(snapshot.IsEnabled("org-flag"));
        Assert.Single(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task PersonFlagDependencyOfAGroupFlagKeepsThePersonKeys()
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(GroupDependsOnPersonDefinitions, "\"v1\"");
        await using var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync(
            "user",
            new AllFeatureFlagsOptions
            {
                OnlyEvaluateLocally = true,
                Groups = [new Group("organization", "org-1")],
                PersonPropertyKeysKnownAbsent = ["tier"]
            },
            CancellationToken.None);

        Assert.True(snapshot.IsEnabled("base-flag"));
        Assert.True(snapshot.IsEnabled("org-flag"));
    }

    public static TheoryData<AllFeatureFlagsOptions> OversizedOptions => new()
    {
        new AllFeatureFlagsOptions { PersonPropertyKeysKnownAbsent = Enumerable.Repeat("plan", 1001).ToArray() },
        new AllFeatureFlagsOptions
        {
            GroupPropertyKeysKnownAbsent = new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["organization"] = Enumerable.Range(0, 1001).Select(i => $"key-{i}").ToArray()
            }
        }
    };

    [Theory]
    [MemberData(nameof(OversizedOptions))]
    public async Task RejectsMoreThan1000KeysWithoutARequest(AllFeatureFlagsOptions options)
    {
        var container = new TestContainer();
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        await using var client = container.Activate<PostHogClient>();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.EvaluateFlagsAsync("user", options, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetAllFeatureFlagsAsync("user", options, CancellationToken.None));
#pragma warning disable CS0618
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetFeatureFlagAsync(
            "plan-flag",
            "user",
            new FeatureFlagOptions
            {
                PersonPropertyKeysKnownAbsent = options.PersonPropertyKeysKnownAbsent,
                GroupPropertyKeysKnownAbsent = options.GroupPropertyKeysKnownAbsent
            },
            CancellationToken.None));
#pragma warning restore CS0618

        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    public static TheoryData<AllFeatureFlagsOptions> NullKeyOptions => new()
    {
        new AllFeatureFlagsOptions { PersonPropertyKeysKnownAbsent = ["plan", null!] },
        new AllFeatureFlagsOptions
        {
            GroupPropertyKeysKnownAbsent = new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["organization"] = ["tier", null!]
            }
        }
    };

    [Theory]
    [MemberData(nameof(NullKeyOptions))]
    public async Task RejectsANullKeyWithoutARequest(AllFeatureFlagsOptions options)
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponse(Definitions);
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        await using var client = container.Activate<PostHogClient>();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.EvaluateFlagsAsync("user", options, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetAllFeatureFlagsAsync("user", options, CancellationToken.None));
#pragma warning disable CS0618
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetFeatureFlagAsync(
            "plan-flag",
            "user",
            new FeatureFlagOptions
            {
                PersonPropertyKeysKnownAbsent = options.PersonPropertyKeysKnownAbsent,
                GroupPropertyKeysKnownAbsent = options.GroupPropertyKeysKnownAbsent
            },
            CancellationToken.None));
#pragma warning restore CS0618

        Assert.Empty(flagsHandler.ReceivedRequests);
    }

    [Fact]
    public async Task AcceptsExactly1000Keys()
    {
        var container = new TestContainer();
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"featureFlags": {}}""");
        await using var client = container.Activate<PostHogClient>();

        await client.GetAllFeatureFlagsAsync(
            "user",
            new AllFeatureFlagsOptions { PersonPropertyKeysKnownAbsent = Enumerable.Repeat("plan", 1000).ToArray() },
            CancellationToken.None);

        Assert.Single(flagsHandler.ReceivedRequests);
    }
}
