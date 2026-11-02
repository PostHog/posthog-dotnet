using PostHog;
using PostHog.Api;
using PostHog.Features;
using PostHog.Json;
using UnitTests.Fakes;

namespace LocalEvaluatorTests;

public class TheUnsupportedCohortFilters
{
    static string Definitions(string cohort) => $$$"""
        {
            "flags": [
                {"key": "local-flag", "active": true,
                 "filters": {"groups": [{"properties": [], "rollout_percentage": 100}]}},
                {"key": "cohort-flag", "active": true,
                 "filters": {"groups": [{"properties": [{"type": "cohort", "key": "id", "value": 42}]}]}}
            ],
            "cohorts": {"42": {{{cohort}}}}
        }
        """;

    [Theory]
    [InlineData("flag", false)]
    [InlineData("flag", true)]
    [InlineData("future_filter", false)]
    [InlineData("future_filter", true)]
    [InlineData("XOR", false)]
    [InlineData("XOR", true)]
    public async Task LoadsOtherDefinitionsAndFallsBackOnlyWhenNeeded(string filterType, bool localOnly)
    {
        var container = new TestContainer("fake-personal-api-key");
        var cohort = $$$"""
            {"type": "AND", "values": [
                {"type": "{{{filterType}}}", "key": "local-flag", "value": true,
                 "operator": "flag_evaluates_to", "dependency_chain": ["local-flag"]}
            ]}
            """;
        var definitionsHandler = container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            Definitions(cohort), "\"definitions\"");
        var remoteHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"cohort-flag": "server-variant"}}""");
        await using var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user", new FeatureFlagOptions
        {
            OnlyEvaluateLocally = localOnly
        }, CancellationToken.None);

        Assert.Single(definitionsHandler.ReceivedRequests);
        Assert.True(snapshot.IsEnabled("local-flag"));
        if (localOnly)
        {
            Assert.Null(snapshot.GetFlag("cohort-flag"));
            Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, snapshot.UnresolvedFlags["cohort-flag"]);
            Assert.Empty(remoteHandler.ReceivedRequests);
        }
        else
        {
            Assert.Equal("server-variant", snapshot.GetFlag("cohort-flag")?.VariantKey);
            Assert.Empty(snapshot.UnresolvedFlags);
            Assert.Single(remoteHandler.ReceivedRequests);
        }
    }

    [Fact]
    public async Task RefreshReplacesDefinitionsEvenWhenACohortContainsAnUnknownFilter()
    {
        var container = new TestContainer("fake-personal-api-key");
        using var httpClient = new HttpClient(container.FakeHttpMessageHandler);
        using var apiClient = container.Activate<PostHogApiClient>(httpClient);
        await using var loader = container.Activate<LocalFeatureFlagsLoader>(apiClient);
        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            """{"flags": [{"key": "old-flag", "active": true}]}""", "\"old\"");
        var previous = await loader.RefreshAsync(CancellationToken.None);
        Assert.NotNull(previous);

        container.FakeHttpMessageHandler.AddLocalEvaluationResponseWithETag(
            Definitions("""{"type": "AND", "values": [{"type": "future_filter"}]}"""), "\"new\"");
        var refreshed = await loader.RefreshAsync(CancellationToken.None);

        Assert.NotNull(refreshed);
        Assert.NotSame(previous, refreshed);
        Assert.Equal(new[] { "local-flag", "cohort-flag" }, refreshed.LocalEvaluationApiResult.Flags.Select(flag => flag.Key));
        Assert.True(refreshed.EvaluateFeatureFlag("local-flag", "user").Value);
        Assert.Same(refreshed, await loader.GetFeatureFlagsForLocalEvaluationAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("AND", "AND", true)]
    [InlineData("AND", "AND", false)]
    [InlineData("AND", "OR", true)]
    [InlineData("AND", "OR", false)]
    [InlineData("OR", "AND", true)]
    [InlineData("OR", "AND", false)]
    [InlineData("OR", "OR", true)]
    [InlineData("OR", "OR", false)]
    public async Task UnknownFiltersAreNotSkippedAlongsideNestedGroups(string groupType, string childGroupType, bool unknownFirst)
    {
        const string unknown = """{"type": "future_filter", "negation": true}""";
        var known = $$$"""
            {"type": "{{{childGroupType}}}", "values": [
                {"type": "person", "key": "plan", "value": "{{{(groupType == "AND" ? "pro" : "free")}}}", "operator": "exact"}
            ]}
            """;
        var children = unknownFirst ? $"{unknown}, {known}" : $"{known}, {unknown}";
        var cohort = $$$"""{"type": "{{{groupType}}}", "values": [{{{children}}}]}""";
        var definitions = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(Definitions(cohort));
        var evaluator = new LocalEvaluator(definitions!);

        var exception = Assert.Throws<InconclusiveMatchException>(() => evaluator.EvaluateFeatureFlag(
            "cohort-flag", "user", personProperties: new() { ["plan"] = "pro" }));

        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, exception.Reason);
        Assert.Equal(UnresolvedFlagReason.UnsupportedDefinition, evaluator.GetDefinitionUnresolvedReason(definitions!.Flags[1]));
    }

    [Theory]
    [InlineData("AND", "free", false, false)]
    [InlineData("AND", "free", false, true)]
    [InlineData("OR", "pro", true, false)]
    [InlineData("OR", "pro", true, true)]
    public async Task UnknownFiltersDoNotOverrideDecisiveSiblingResults(string groupType, string plan, bool expected, bool nested)
    {
        var known = $$$"""{"type": "person", "key": "plan", "value": "{{{plan}}}", "operator": "exact"}""";
        if (nested)
        {
            known = $$$"""{"type": "{{{(groupType == "AND" ? "OR" : "AND")}}}", "values": [{{{known}}}]}""";
        }
        var cohort = $$$"""
            {"type": "{{{groupType}}}", "values": [
                {"type": "future_filter", "negation": true},
                {{{known}}}
            ]}
            """;
        var definitions = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(Definitions(cohort));
        var evaluator = new LocalEvaluator(definitions!);

        var result = evaluator.EvaluateFeatureFlag("cohort-flag", "user", personProperties: new() { ["plan"] = "pro" });

        Assert.Equal(expected, result.Value);
    }
}
