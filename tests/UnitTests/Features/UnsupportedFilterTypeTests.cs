using PostHog;
using PostHog.Api;
using PostHog.Features;
using UnitTests.Fakes;

namespace LocalEvaluatorTests;

public class TheUnsupportedFilterTypes
{
    const string CreatedAtAfter2025 = "\"key\": \"created_at\", \"operator\": \"is_date_after\", \"value\": \"2025-01-01\"";
    const string PersonMetadataLeaf = """{"type": "person_metadata", """ + CreatedAtAfter2025 + "}";
    const string CohortCondition = """{"properties": [{"type": "cohort", "key": "id", "value": 7}], "rollout_percentage": 100}""";

    static string Definitions(string conditions, string cohorts = "{}") => $$$"""
        {
            "flags": [
                {"key": "flag", "active": true, "filters": {"groups": [{{{conditions}}}]}},
                {"key": "sibling", "active": true, "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}}
            ],
            "cohorts": {{{cohorts}}}
        }
        """;

    static string Cohort(string groupType, string values) => $$$"""{"7": {"type": "{{{groupType}}}", "values": [{{{values}}}]}}""";

    sealed record Evaluation(
        bool? Flag,
        bool Sibling,
        IReadOnlyDictionary<string, UnresolvedFlagReason> UnresolvedFlags,
        int RemoteRequests);

    static async Task<Evaluation> EvaluateAsync(
        string definitions,
        Dictionary<string, object?> personProperties,
        bool onlyEvaluateLocally = true)
    {
        var container = new TestContainer("fake-personal-api-key");
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(definitions, "\"definitions\"");
        var remoteHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"flag": "server-variant", "sibling": "server-variant"}}""");
        await using var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user", new FeatureFlagOptions
        {
            OnlyEvaluateLocally = onlyEvaluateLocally,
            PersonProperties = personProperties
        }, CancellationToken.None);

        return new Evaluation(
            snapshot.GetFlag("flag")?.IsEnabled,
            snapshot.IsEnabled("sibling"),
            snapshot.UnresolvedFlags.ToDictionary(pair => pair.Key, pair => pair.Value),
            remoteHandler.ReceivedRequests.Count);
    }

    [Theory]
    [InlineData("""
                "type": "person",
                """)]
    [InlineData("")]
    [InlineData("""
                "type": null,
                """)]
    [InlineData("""
                "type": "",
                """)]
    public async Task MatchesPersonFiltersWithOmittedNullOrEmptyType(string type)
    {
        var result = await EvaluateAsync(
            Definitions($$$"""{"properties": [{{{{type}}} {{{CreatedAtAfter2025}}}}], "rollout_percentage": 100}"""),
            new() { ["created_at"] = "2026-06-01" });

        Assert.True(result.Flag);
        Assert.Empty(result.UnresolvedFlags);
    }

    [Theory]
    [InlineData("""
                "type": null,
                """)]
    [InlineData("""
                "type": "",
                """)]
    [InlineData("")]
    public async Task MatchesCohortLeavesWithOmittedNullOrEmptyTypeAsPersonFilters(string type)
    {
        var result = await EvaluateAsync(
            Definitions(CohortCondition, Cohort("AND", $$$"""{{{{type}}} {{{CreatedAtAfter2025}}}}""")),
            new() { ["created_at"] = "2026-06-01" });

        Assert.True(result.Flag);
        Assert.Empty(result.UnresolvedFlags);
    }

    [Theory]
    [InlineData("person_metadata", "2026-06-01")]
    [InlineData("person_metadata", "2020-06-01")]
    [InlineData("person_metadata", null)]
    [InlineData("future_type", "2026-06-01")]
    public async Task LeavesUnsupportedConditionFilterTypesUnresolved(string type, string? createdAt)
    {
        var result = await EvaluateAsync(
            Definitions($$$"""{"properties": [{"type": "{{{type}}}", {{{CreatedAtAfter2025}}}}], "rollout_percentage": 100}"""),
            createdAt is null ? new() : new() { ["created_at"] = createdAt });

        Assert.Null(result.Flag);
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, result.UnresolvedFlags["flag"]);
        Assert.True(result.Sibling);
        Assert.Equal(0, result.RemoteRequests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("[]")]
    public async Task LeavesNonStringConditionFilterTypesUnresolved(string type)
    {
        var result = await EvaluateAsync(
            Definitions($$$"""{"properties": [{"type": {{{type}}}, {{{CreatedAtAfter2025}}}}], "rollout_percentage": 100}"""),
            new() { ["created_at"] = "2026-06-01" });

        Assert.Null(result.Flag);
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, result.UnresolvedFlags["flag"]);
        Assert.True(result.Sibling);
        Assert.Equal(0, result.RemoteRequests);
    }

    [Theory]
    [InlineData("2026-06-01")]
    [InlineData("2020-06-01")]
    [InlineData(null)]
    public async Task LeavesUnsupportedCohortLeavesUnresolved(string? createdAt)
    {
        var result = await EvaluateAsync(
            Definitions(CohortCondition, Cohort("AND", PersonMetadataLeaf)),
            createdAt is null ? new() : new() { ["created_at"] = createdAt });

        Assert.Null(result.Flag);
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, result.UnresolvedFlags["flag"]);
    }

    [Fact]
    public async Task DoesNotMatchANegatedUnsupportedCohortLeaf()
    {
        var negatedLeaf = $$$"""{"type": "person_metadata", {{{CreatedAtAfter2025}}}, "negation": true}""";

        var result = await EvaluateAsync(
            Definitions(CohortCondition, Cohort("AND", negatedLeaf)),
            new() { ["created_at"] = "2020-06-01" });

        Assert.Null(result.Flag);
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, result.UnresolvedFlags["flag"]);
    }

    [Fact]
    public async Task DoesNotMatchAnUnsupportedCohortLeafWithNotInOperator()
    {
        const string notInCondition = """{"properties": [{"type": "cohort", "key": "id", "value": 7, "operator": "not_in"}], "rollout_percentage": 100}""";

        var result = await EvaluateAsync(
            Definitions(notInCondition, Cohort("AND", PersonMetadataLeaf)),
            new() { ["created_at"] = "2020-06-01" });

        Assert.Null(result.Flag);
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, result.UnresolvedFlags["flag"]);
    }

    [Fact]
    public async Task LetsAnotherCohortLeafDecideTheGroup()
    {
        var leaves = PersonMetadataLeaf + """, {"type": "person", "key": "plan", "operator": "exact", "value": "pro"}""";

        var result = await EvaluateAsync(
            Definitions(CohortCondition, Cohort("AND", leaves)),
            new() { ["plan"] = "free", ["created_at"] = "2026-06-01" },
            onlyEvaluateLocally: false);

        Assert.False(result.Flag);
        Assert.Empty(result.UnresolvedFlags);
        Assert.Equal(0, result.RemoteRequests);
    }

    [Fact]
    public async Task LetsADefinitivelyFalseFilterDecideTheCondition()
    {
        const string condition = """
            {"properties": [
                {"type": "future_type", "key": "region", "operator": "exact", "value": "eu"},
                {"type": "person", "key": "plan", "operator": "exact", "value": "pro"}
            ], "rollout_percentage": 100}
            """;

        var result = await EvaluateAsync(
            Definitions(condition),
            new() { ["plan"] = "free", ["region"] = "eu" },
            onlyEvaluateLocally: false);

        Assert.False(result.Flag);
        Assert.Empty(result.UnresolvedFlags);
        Assert.Equal(0, result.RemoteRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LetsALaterConditionMatchAfterAnUnsupportedConditionFilter(bool inCohort)
    {
        var firstCondition = inCohort
            ? CohortCondition
            : $$$"""{"properties": [{"type": "person_metadata", {{{CreatedAtAfter2025}}}}], "rollout_percentage": 100}""";
        var conditions = firstCondition + """, {"properties": [], "rollout_percentage": 100}""";

        var result = await EvaluateAsync(
            Definitions(conditions, inCohort ? Cohort("AND", PersonMetadataLeaf) : "{}"),
            new() { ["created_at"] = "2026-06-01" },
            onlyEvaluateLocally: false);

        Assert.True(result.Flag);
        Assert.Empty(result.UnresolvedFlags);
        Assert.Equal(0, result.RemoteRequests);
    }

    [Theory]
    [InlineData("person_metadata")]
    [InlineData("future_type")]
    public async Task ReportsUnsupportedConditionFilterTypesInTheDefinitionReason(string type)
    {
        var definitions = await PostHog.Json.JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(
            Definitions($$$"""{"properties": [{"type": "{{{type}}}", {{{CreatedAtAfter2025}}}}], "rollout_percentage": 100}"""));
        var evaluator = new LocalEvaluator(definitions!);

        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, evaluator.GetDefinitionUnresolvedReason(definitions!.Flags[0]));
        Assert.Null(evaluator.GetDefinitionUnresolvedReason(definitions.Flags[1]));
    }

    [Fact]
    public async Task RefreshLoadsDefinitionsWithAnUnsupportedConditionFilterType()
    {
        var container = new TestContainer("fake-personal-api-key");
        using var httpClient = new HttpClient(container.FakeHttpMessageHandler);
        using var apiClient = container.Activate<PostHogApiClient>(httpClient);
        await using var loader = container.Activate<LocalFeatureFlagsLoader>(apiClient);
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            Definitions($$$"""{"properties": [{"type": "person_metadata", {{{CreatedAtAfter2025}}}}], "rollout_percentage": 100}"""),
            "\"definitions\"");

        var loaded = await loader.RefreshAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(new[] { "flag", "sibling" }, loaded.LocalEvaluationApiResult.Flags.Select(flag => flag.Key));
        Assert.True(loaded.EvaluateFeatureFlag("sibling", "user").Value);
    }

    [Fact]
    public async Task ReadsTheSameUnsupportedConditionFilterAsAnEqualDefinition()
    {
        var json = Definitions($$$"""{"properties": [{"type": "person_metadata", {{{CreatedAtAfter2025}}}}], "rollout_percentage": 100}""");

        var first = await PostHog.Json.JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(json);
        var second = await PostHog.Json.JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(json);

        Assert.Equal(first!.Flags[0], second!.Flags[0]);
    }
}
