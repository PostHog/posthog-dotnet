using System.Text.Json;
using PostHog;
using PostHog.Api;
using PostHog.Features;
using PostHog.Json;
using UnitTests.Fakes;

namespace LocalEvaluatorTests;

public class TheRegexCompatibilityBoundary
{
    static LocalEvaluator CreateEvaluator(string pattern, ComparisonOperator comparison, bool negateInCohort = false) =>
        new(CreateDefinitions(pattern, comparison, negateInCohort));

    static LocalEvaluationApiResult CreateDefinitions(string pattern, ComparisonOperator comparison, bool negateInCohort = false)
    {
        var property = new PropertyFilter
        {
            Type = FilterType.Person,
            Key = "property",
            Value = new PropertyFilterValue(pattern),
            Operator = comparison,
            Negation = negateInCohort
        };
        return new LocalEvaluationApiResult
        {
            Flags = [new LocalFeatureFlag
            {
                Id = 42,
                TeamId = 23,
                Key = "regex",
                Active = true,
                Filters = new FeatureFlagFilters
                {
                    Groups = [new FeatureFlagGroup
                    {
                        Properties = [negateInCohort ? new PropertyFilter
                        {
                            Type = FilterType.Cohort,
                            Key = "id",
                            Value = new PropertyFilterValue(1),
                            Operator = ComparisonOperator.In
                        } : property]
                    }]
                }
            }],
            GroupTypeMapping = new Dictionary<string, string>(),
            Cohorts = new Dictionary<string, FilterSet>
            {
                ["1"] = new FilterSet { Type = FilterType.And, Values = [property] }
            }
        };
    }

    static LocalEvaluationApiResult CreateCohortDefinitions(
        string pattern,
        ComparisonOperator comparison,
        string structure,
        FilterType groupType = FilterType.And,
        bool negateLeaf = true)
    {
        var definitions = CreateDefinitions(pattern, comparison, negateInCohort: true);
        var leaf = new PropertyFilter
        {
            Type = FilterType.Person,
            Key = "property",
            Value = new PropertyFilterValue(pattern),
            Operator = comparison,
            Negation = negateLeaf
        };
        var group = new FilterSet { Type = groupType, Values = [leaf] };
        var cohorts = new Dictionary<string, FilterSet> { ["1"] = group };
        if (structure == "nested")
        {
            cohorts["1"] = new FilterSet { Type = groupType, Values = [group] };
        }
        else if (structure is "reference" or "negated-reference")
        {
            cohorts["2"] = group;
            cohorts["1"] = new FilterSet
            {
                Type = groupType,
                Values = [new PropertyFilter
                {
                    Type = FilterType.Cohort,
                    Key = "id",
                    Value = new PropertyFilterValue(2),
                    Operator = ComparisonOperator.In,
                    Negation = structure == "negated-reference"
                }]
            };
        }
        return definitions with { Cohorts = cohorts };
    }

    static string CreateCohortResponse(string pattern, ComparisonOperator comparison, string cohortOperator = "in") =>
        $$$"""
        {
            "flags": [{"id": 42, "key": "regex", "active": true,
                "filters": {"groups": [{"properties": [
                    {"type": "cohort", "key": "id", "value": 1, "operator": "{{{cohortOperator}}}"}
                ]}]}}],
            "cohorts": {"1": {"type": "AND", "values": [
                {"type": "person", "key": "property", "value": {{{JsonSerializer.Serialize(pattern)}}},
                 "operator": "{{{(comparison is ComparisonOperator.Regex ? "regex" : "not_regex")}}}", "negation": true}
            ]}}
        }
        """;

    [Theory]
    [InlineData(ComparisonOperator.Regex, "leaf")]
    [InlineData(ComparisonOperator.NotRegex, "leaf")]
    [InlineData(ComparisonOperator.Regex, "nested")]
    [InlineData(ComparisonOperator.NotRegex, "nested")]
    [InlineData(ComparisonOperator.Regex, "reference")]
    [InlineData(ComparisonOperator.NotRegex, "reference")]
    [InlineData(ComparisonOperator.Regex, "negated-reference")]
    [InlineData(ComparisonOperator.NotRegex, "negated-reference")]
    public void InvalidCohortLeavesRemainInconclusiveBeforeNegation(ComparisonOperator comparison, string structure)
    {
        foreach (var groupType in new[] { FilterType.And, FilterType.Or })
            foreach (var negateLeaf in new[] { false, true })
            {
                var evaluator = new LocalEvaluator(CreateCohortDefinitions("[", comparison, structure, groupType, negateLeaf));
                var properties = new Dictionary<string, object?> { ["property"] = "value" };

                Assert.Throws<InconclusiveMatchException>(() =>
                    evaluator.EvaluateFeatureFlag("regex", "user", personProperties: properties));
                var (flags, fallbackToRemote) = evaluator.EvaluateAllFlags("user", personProperties: properties);
                Assert.True(fallbackToRemote);
                Assert.Empty(flags);
            }
    }

    [Theory]
    [InlineData(ComparisonOperator.Regex, false, "in")]
    [InlineData(ComparisonOperator.NotRegex, false, "in")]
    [InlineData(ComparisonOperator.Regex, true, "in")]
    [InlineData(ComparisonOperator.NotRegex, true, "in")]
    [InlineData(ComparisonOperator.Regex, false, "not_in")]
    [InlineData(ComparisonOperator.NotRegex, false, "not_in")]
    [InlineData(ComparisonOperator.Regex, true, "not_in")]
    [InlineData(ComparisonOperator.NotRegex, true, "not_in")]
    public async Task InvalidCohortLeavesUsePublicFallbackUnlessLocalOnly(
        ComparisonOperator comparison, bool localOnly, string cohortOperator)
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        var localHandler = container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            CreateCohortResponse("[", comparison, cohortOperator));
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"regex": "server-variant"}}""");
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user", new AllFeatureFlagsOptions
        {
            OnlyEvaluateLocally = localOnly,
            FlagKeysToEvaluate = ["regex"],
            PersonProperties = new Dictionary<string, object?> { ["property"] = "value" }
        }, CancellationToken.None);

        Assert.Single(localHandler.ReceivedRequests);
        if (localOnly)
        {
            Assert.Null(snapshot.GetFlag("regex"));
            Assert.Empty(flagsHandler.ReceivedRequests);
        }
        else
        {
            Assert.Equal("server-variant", snapshot.GetFlag("regex")?.VariantKey);
            Assert.Single(flagsHandler.ReceivedRequests);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidNegatedCohortStaysLocalUnlessPropertyMissing(bool missingProperty)
    {
        var container = new TestContainer(personalApiKey: "fake-personal-api-key");
        var localHandler = container.FakeHttpMessageHandler.AddLocalEvaluationResponse(
            CreateCohortResponse("^hit$", ComparisonOperator.NotRegex));
        var flagsHandler = container.FakeHttpMessageHandler.AddFlagsResponse(
            """{"featureFlags": {"regex": "server-variant"}}""");
        var client = container.Activate<PostHogClient>();

        var snapshot = await client.EvaluateFlagsAsync("user", new AllFeatureFlagsOptions
        {
            FlagKeysToEvaluate = ["regex"],
            PersonProperties = missingProperty ? [] : new Dictionary<string, object?> { ["property"] = "hit" }
        }, CancellationToken.None);

        Assert.Single(localHandler.ReceivedRequests);
        if (missingProperty)
        {
            Assert.Equal("server-variant", snapshot.GetFlag("regex")?.VariantKey);
            Assert.Single(flagsHandler.ReceivedRequests);
        }
        else
        {
            Assert.True(snapshot.IsEnabled("regex"));
            Assert.Null(snapshot.GetFlag("regex")?.VariantKey);
            Assert.Empty(flagsHandler.ReceivedRequests);
        }
    }

    [Theory]
    [InlineData("pro", "a pro plan", true)]
    [InlineData("^pro$", "a pro plan", false)]
    [InlineData("^pro$", "PRO", false)]
    [InlineData("^\\d+$", "123", true)]
    [InlineData("^[\\w.+-]+@example\\.com$", "a.b+tag@example.com", true)]
    [InlineData("^(?:ab|cd){2,3}$", "abcdab", true)]
    [InlineData("^a*?$", "aa", true)]
    [InlineData("^a+?$", "aa", true)]
    [InlineData("^a??$", "a", true)]
    [InlineData("^a{1,2}?$", "aa", true)]
    [InlineData("^foo$", "foo\n", false)]
    [InlineData("foo$\\n", "foo\n", false)]
    [InlineData("^foo$", "before\nfoo\nafter", false)]
    [InlineData("^.$", "\r", true)]
    [InlineData("", "é", true)]
    [InlineData("", "😀", true)]
    [InlineData("", null, true)]
    [InlineData("^null$", null, true)]
    [InlineData("[\\b]", "\b", true)]
    [InlineData("^\\x41ab$", "Aab", true)]
    [InlineData("^a\\-b$", "a-b", true)]
    [InlineData("\\@example\\.com$", "a@example.com", true)]
    [InlineData("^a\\:b$", "a:b", true)]
    [InlineData("^a\\_b$", "a_b", true)]
    [InlineData("^a\\=b$", "a=b", true)]
    [InlineData("^a\\ b$", "a b", true)]
    [InlineData("^\\^?$", "", true)]
    [InlineData("^\\${2}$", "$$", true)]
    [InlineData("^\\({2}$", "((", true)]
    [InlineData("^\\|{2}$", "||", true)]
    [InlineData("^[\\^$?]+$", "$?^", true)]
    [InlineData("^[\\w-]+$", "a-b", true)]
    public void KeepsPortableSearchesLocal(string pattern, string? input, bool matched)
    {
        foreach (var comparison in new[] { ComparisonOperator.Regex, ComparisonOperator.NotRegex })
        {
            var evaluator = CreateEvaluator(pattern, comparison);
            var properties = new Dictionary<string, object?> { ["property"] = input };
            var expected = comparison is ComparisonOperator.Regex ? matched : !matched;

            Assert.Equal(expected, evaluator.EvaluateFeatureFlag("regex", "user", personProperties: properties));
            var (flags, fallbackToRemote) = evaluator.EvaluateAllFlags("user", personProperties: properties);
            Assert.False(fallbackToRemote);
            Assert.Equal(expected, flags["regex"].IsEnabled);
        }
    }

    [Theory]
    [InlineData("[", "value")]
    [InlineData("(", "value")]
    [InlineData("*", "value")]
    [InlineData("?*", "value")]
    [InlineData("[", "é")]
    [InlineData("[", "\r")]
    public void InvalidPortablePatternsNeverMatchEitherOperator(string pattern, string input)
    {
        foreach (var comparison in new[] { ComparisonOperator.Regex, ComparisonOperator.NotRegex })
        {
            var evaluator = CreateEvaluator(pattern, comparison);
            var properties = new Dictionary<string, object?> { ["property"] = input };
            Assert.False(evaluator.EvaluateFeatureFlag("regex", "user", personProperties: properties).Value);
            var (flags, fallbackToRemote) = evaluator.EvaluateAllFlags("user", personProperties: properties);
            Assert.False(fallbackToRemote);
            Assert.False(flags["regex"].IsEnabled);
        }
    }

    [Theory]
    [InlineData("^\\d+$", "𝟙")]
    [InlineData("^\\p{L}+$", "𠀀")]
    [InlineData("^\\p{Greek}+$", "α")]
    [InlineData("^\\p{L}+$", "ascii")]
    [InlineData("^.$", "😀")]
    [InlineData("^😀?$", "")]
    [InlineData("^\\w+$", "é")]
    [InlineData("^[a-z&&[^aeiou]]+$", "bcdf")]
    [InlineData("^[a&&b]$", "a")]
    [InlineData("^[a-z--b]$", "b")]
    [InlineData("^[a~~b]$", "~")]
    [InlineData("^[[:alpha:]]+$", "foo")]
    [InlineData("(?i)^null$", "NULL")]
    [InlineData("a(?=b)", "ab")]
    [InlineData("(?<=a+)b", "aab")]
    [InlineData("(a)\\1", "aa")]
    [InlineData("\\Afoo\\z", "foo")]
    [InlineData("\\q", "q")]
    [InlineData("^?https?://", "https://a")]
    [InlineData("$?", "x")]
    [InlineData("()+", "x")]
    [InlineData("(?:)*", "x")]
    [InlineData("(?:){2}", "x")]
    [InlineData("{2}", "x")]
    [InlineData("a|{2}", "x")]
    [InlineData("({2})", "x")]
    [InlineData("(?:{2})", "x")]
    [InlineData("a{0,4294967296}", "x")]
    [InlineData("a{4294967296,}", "x")]
    [InlineData("a++", "aa")]
    [InlineData("a**", "aa")]
    [InlineData("a+*", "aa")]
    [InlineData("a?*", "aa")]
    [InlineData("a*?*", "aa")]
    [InlineData("a???", "aa")]
    [InlineData("a*??", "aa")]
    [InlineData("a+??", "aa")]
    [InlineData("a{2}??", "aa")]
    [InlineData("a{2}+", "aa")]
    [InlineData("\\<foo\\>", "foo")]
    [InlineData("^\\<$", "<")]
    [InlineData("^\\>$", ">")]
    [InlineData("a{,2}", "aa")]
    [InlineData("a*{2}", "aa")]
    [InlineData("{foo}", "{foo}")]
    [InlineData("[\\s-9]", "-")]
    [InlineData("[a-\\d]", "-")]
    [InlineData("[]", "a")]
    [InlineData("[^]", "a")]
    [InlineData("[\\@]", "@")]
    [InlineData("[\\B]", "B")]
    [InlineData(".*\\bbot\\b.*", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(".*\\Bbot\\B.*", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void UnsupportedSearchesReachFallbackIncludingNegation(string pattern, string input)
    {
        foreach (var comparison in new[] { ComparisonOperator.Regex, ComparisonOperator.NotRegex })
            foreach (var negateInCohort in new[] { false, true })
            {
                var evaluator = CreateEvaluator(pattern, comparison, negateInCohort);
                var properties = new Dictionary<string, object?> { ["property"] = input };
                Assert.Throws<InconclusiveMatchException>(() =>
                    evaluator.EvaluateFeatureFlag("regex", "user", personProperties: properties));
                var (flags, fallbackToRemote) = evaluator.EvaluateAllFlags("user", personProperties: properties);
                Assert.True(fallbackToRemote);
                Assert.Empty(flags);
            }
    }

    [Fact]
    public void NativeExecutionTimeoutsReachFallbackWithoutNegation()
    {
        foreach (var comparison in new[] { ComparisonOperator.Regex, ComparisonOperator.NotRegex })
        {
            var evaluator = CreateEvaluator("^(a+)+$", comparison);
            var properties = new Dictionary<string, object?> { ["property"] = new string('a', 200) + "!" };
            Assert.Throws<InconclusiveMatchException>(() =>
                evaluator.EvaluateFeatureFlag("regex", "user", personProperties: properties));
            var (flags, fallbackToRemote) = evaluator.EvaluateAllFlags("user", personProperties: properties);
            Assert.True(fallbackToRemote);
            Assert.Empty(flags);
        }
    }

    [Theory]
    [InlineData(ComparisonOperator.Regex)]
    [InlineData(ComparisonOperator.NotRegex)]
    public void MissingPropertiesStillReachFallback(ComparisonOperator comparison)
    {
        var evaluator = CreateEvaluator("", comparison);
        Assert.Throws<InconclusiveMatchException>(() => evaluator.EvaluateFeatureFlag("regex", "user", personProperties: []));
        var (flags, fallbackToRemote) = evaluator.EvaluateAllFlags("user", personProperties: []);
        Assert.True(fallbackToRemote);
        Assert.Empty(flags);
    }
}
