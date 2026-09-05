using System.Text.Json;
using PostHog;
using PostHog.Api;
using PostHog.Exceptions;
using PostHog.Features;
using PostHog.Json;

namespace LocalEvaluatorTests;

public class VersionedPropertyMatchingTests
{
    public static TheoryData<string, string, bool, bool> MatchingCases => new()
    {
        { "false", "\"banana\"", true, false },
        { "false", "0", true, false },
        { "[\"true\",\"false\"]", "\"true\"", false, true },
        { "[\"true\",\"false\"]", "\"pro\"", true, false },
        { "[]", "true", true, true },
        { "[]", "[]", true, true },
        { "true", "[true]", true, false },
        { "false", "\"FALSE\"", true, true },
        { "false", "null", true, false },
        { "false", "\"\"", true, false },
        { "[]", "[true,[\"TRUE\",[]]]", true, true },
        { "[]", "[true,[false]]", false, false },
        { "[]", "false", false, false },
        { "[]", "null", false, false },
        { "[]", "0", false, false },
        { "[]", "1", false, false },
        { "[]", "\"banana\"", false, false },
        { "[\"FREE\",\"PRO\"]", "\"pro\"", true, true },
        { "[false,\"PRO\"]", "\"pro\"", true, true },
        { "[[true],\"PRO\"]", "[true]", true, true },
        { "[\"TrUe\",\"FALSE\"]", "true", false, true },
        { "[\"TrUe\",\"FALSE\"]", "false", true, true },
        { "[\"İ\",false]", "\"i̇\"", true, true },
        { "\"ΟΔΟΣ\"", "\"οδος\"", true, true },
        { "true", "true", true, true },
        { "false", "false", true, true },
        { "[[true],[false]]", "[true]", false, true }
    };

    [Theory]
    [MemberData(nameof(MatchingCases))]
    public void SelectsSnapshotSemanticsForExactAndIsNot(string filterJson, string propertyJson, bool legacy, bool explicitMatch)
    {
        using var property = JsonDocument.Parse(propertyJson);
        object? value = property.RootElement.ValueKind == JsonValueKind.Null ? null : property.RootElement;
        foreach (var version in new int?[] { null, 1, 2, 0, 3 })
        {
            foreach (var comparison in new[] { "exact", "is_not" })
            {
                var evaluator = new LocalEvaluator(ParseDefinitions(filterJson, comparison, version));
                var expected = version == 2 ? explicitMatch : legacy;
                if (comparison == "is_not")
                {
                    expected = !expected;
                }
                Assert.Equal(expected, evaluator.EvaluateFeatureFlag("test", "person", personProperties: new() { ["value"] = value }));
            }
        }
    }

    [Theory]
    [InlineData("[null,\"x\"]", "null")]
    [InlineData("[\"NULL\"]", "null")]
    public void ExplicitListMembershipIncludesKnownNull(string filterJson, string propertyJson)
    {
        using var property = JsonDocument.Parse(propertyJson);
        var evaluator = new LocalEvaluator(ParseDefinitions(filterJson, "exact", 2));
        Assert.Equal(true, evaluator.EvaluateFeatureFlag("test", "person", personProperties: new() { ["value"] = null }));
        Assert.Equal(true, evaluator.EvaluateFeatureFlag("test", "person", personProperties: new() { ["value"] = property.RootElement }));
    }

    [Theory]
    [InlineData(1, "exact")]
    [InlineData(2, "exact")]
    [InlineData(1, "is_not")]
    [InlineData(2, "is_not")]
    public void MissingPropertyAndUnsupportedNullFilterRemainInconclusive(int version, string comparison)
    {
        var evaluator = new LocalEvaluator(ParseDefinitions("false", comparison, version));
        Assert.Throws<InconclusiveMatchException>(() => evaluator.EvaluateFeatureFlag("test", "person", personProperties: new()));
        evaluator = new LocalEvaluator(ParseDefinitions("null", comparison, version));
        Assert.Throws<InconclusiveMatchException>(() => evaluator.EvaluateFeatureFlag("test", "person", personProperties: new() { ["value"] = null }));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void PersonGroupRecursiveCohortAndDependencyShareSnapshot(int? version, bool expected)
    {
        var definitions = ParseDefinitions("false", "exact", version);
        var person = definitions.Flags[0];
        var leaf = person.Filters!.Groups![0].Properties![0];
        var group = person with
        {
            Key = "group",
            Filters = person.Filters with
            {
                AggregationGroupTypeIndex = 0,
                Groups = [new FeatureFlagGroup { Properties = [leaf with { Type = FilterType.Group }] }]
            }
        };
        var cohort = person with
        {
            Key = "cohort",
            Filters = new FeatureFlagFilters
            {
                Groups = [new FeatureFlagGroup
                {
                    Properties = [new PropertyFilter { Key = "id", Type = FilterType.Cohort, Value = new PropertyFilterValue(123L) }]
                }]
            }
        };
        var dependent = person with
        {
            Key = "dependent",
            Filters = new FeatureFlagFilters
            {
                Groups = [new FeatureFlagGroup
                {
                    Properties = [new PropertyFilter
                    {
                        Key = "test", Type = FilterType.Flag, Operator = ComparisonOperator.FlagEvaluatesTo,
                        Value = new PropertyFilterValue(true), DependencyChain = ["test"]
                    }]
                }]
            }
        };
        definitions = definitions with
        {
            Flags = [person, group, cohort, dependent],
            GroupTypeMapping = new Dictionary<string, string> { ["0"] = "company" },
            Cohorts = new Dictionary<string, FilterSet>
            {
                ["123"] = new FilterSet
                {
                    Type = FilterType.And,
                    Values = [new FilterSet { Type = FilterType.And, Values = [leaf] }]
                }
            }
        };
        var evaluator = new LocalEvaluator(definitions);
        var properties = new Dictionary<string, object?> { ["value"] = "banana" };
        var groups = new GroupCollection { new Group("company", "acme", properties) };
        foreach (var flag in definitions.Flags)
        {
            Assert.Equal(expected, evaluator.EvaluateFeatureFlag(flag.Key, "person", groups, properties));
        }
        var (all, fallback) = evaluator.EvaluateAllFlags("person", groups, properties);
        Assert.False(fallback);
        Assert.Equal(4, all.Count);
        Assert.All(all.Values, flag => Assert.Equal(expected, flag.IsEnabled));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    public void DefinitionsMetadataSurvivesSerialization(int? version)
    {
        var definitions = ParseDefinitions("false", "exact", version) with { Flags = [] };
        var restored = JsonSerializer.Deserialize<LocalEvaluationApiResult>(JsonSerializer.Serialize(definitions, JsonSerializerHelper.Options), JsonSerializerHelper.Options)!;
        Assert.Equal(version, restored.PropertyMatchingVersion);
        Assert.Equal(definitions, restored);
        Assert.NotEqual(definitions, definitions with { PropertyMatchingVersion = version == 2 ? 1 : 2 });
    }

    [Fact]
    public void ExplicitMatchingUsesCanonicalNumericRepresentationWithoutLegacyNumericListCoercion()
    {
        using var document = JsonDocument.Parse("[1.0]");
        var filter = PropertyFilterValue.Create(document.RootElement)!;
        Assert.True(filter.IsExactMatch(1)); // Existing legacy numeric-list fallback remains intact.
        Assert.False(filter.IsExactMatch(1, 2));
        Assert.True(filter.IsExactMatch(1.0, 2));
        Assert.True(new PropertyFilterValue(1L).IsExactMatch(1, 2));
        Assert.False(new PropertyFilterValue(false).IsExactMatch(0.0, 2));
    }

    public static TheoryData<decimal, string, bool> DecimalMatchingCases => new()
    {
        { 1.00m, "[1.00]", true },
        { 1.00m, "[\"1.0\"]", true },
        { 1.00m, "\"1.0\"", true },
        { 1.00m, "[\"1.00\"]", false },
        { 1.00m, "[1]", false },
        { 1m, "[1]", true },
        { 1m, "[1.0]", false },
        { 1.2300m, "[1.23]", true },
        { 0.00m, "[0.0]", true },
        { -1.00m, "[-1.0]", true },
        { 0.00000100m, "[1e-6]", true },
        { 1.2345678901234567890123456789m, "[1.2345678901234567890123456789]", true },
        { 18446744073709551615m, "[18446744073709551615]", true },
        { decimal.MaxValue, "[79228162514264337593543950335]", true }
    };

    [Theory]
    [MemberData(nameof(DecimalMatchingCases))]
    public void ExplicitDecimalMatchingUsesWireRepresentation(decimal property, string filterJson, bool exact)
    {
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(property));
        foreach (var comparison in new[] { "exact", "is_not" })
        {
            var evaluator = new LocalEvaluator(ParseDefinitions(filterJson, comparison, 2));
            var expected = comparison == "exact" ? exact : !exact;
            Assert.Equal(expected, evaluator.EvaluateFeatureFlag("test", "person", personProperties: new() { ["value"] = wire.RootElement }));
            Assert.Equal(expected, evaluator.EvaluateFeatureFlag("test", "person", personProperties: new() { ["value"] = property }));
        }
    }

    [Fact]
    public void DecimalNormalizationPreservesLegacyAndOtherOperators()
    {
        using var document = JsonDocument.Parse("[1.00]");
        var filter = PropertyFilterValue.Create(document.RootElement)!;
        Assert.True(filter.IsExactMatch(1.00m));
        foreach (var version in new int?[] { null, 1, 0, 3 })
        {
            Assert.True(filter.IsExactMatch(1.00m, version));
            Assert.True(new PropertyFilterValue("1.00").IsExactMatch(1.00m, version));
        }
        Assert.True(new PropertyFilterValue("1.00").IsContainedBy(1.00m, StringComparison.Ordinal));
    }

    internal static LocalEvaluationApiResult ParseDefinitions(string filterJson, string comparison, int? version) =>
        JsonSerializer.Deserialize<LocalEvaluationApiResult>(DefinitionsJson(filterJson, comparison, version), JsonSerializerHelper.Options)!;

    internal static string DefinitionsJson(string filterJson, string comparison, int? version) => $$"""
        {
            {{(version.HasValue ? $"\"property_matching_version\": {version.Value}," : "")}}
            "flags": [{
                "key": "test", "active": true, "version": 2,
                "filters": {"groups": [{"properties": [
                    {"key": "value", "type": "person", "operator": "{{comparison}}", "value": {{filterJson}}}
                ]}]}
            }]
        }
        """;
}
