using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PostHog;
using PostHog.Api;
using PostHog.Json;
using UnitTests.Fakes;
using Xunit.Sdk;

namespace ServerSdkSnapshotTests;

public class SnapshotNormalizationTests
{
    [Fact]
    public void PreservesGuidRelationshipsWhileCanonicalizingObjects()
    {
        const string expected = """
            {
              "first": "00000000-0000-0000-0000-000000000001",
              "items": [
                "00000000-0000-0000-0000-000000000002",
                "00000000-0000-0000-0000-000000000001"
              ]
            }
            """;
        const string actual = """
            {
              "items": [
                "80e0f2e4-f1ad-4e6e-bd86-0b6355e0180b",
                "634b8691-c087-4d2d-94af-0e12db8b47b2"
              ],
              "first": "634b8691-c087-4d2d-94af-0e12db8b47b2"
            }
            """;

        JsonAssert.EqualSnapshot(expected, actual);
    }

    [Fact]
    public void DistinctGuidsDoNotMatchOneRepeatedGuid()
    {
        const string expected = """
            [
              "00000000-0000-0000-0000-000000000001",
              "00000000-0000-0000-0000-000000000002"
            ]
            """;
        const string actual = """
            [
              "634b8691-c087-4d2d-94af-0e12db8b47b2",
              "634b8691-c087-4d2d-94af-0e12db8b47b2"
            ]
            """;

        Assert.Throws<EqualException>(() => JsonAssert.EqualSnapshot(expected, actual));
    }

    [Fact]
    public void PreservesArrayOrder()
    {
        Assert.Throws<EqualException>(() => JsonAssert.EqualSnapshot("[1, 2]", "[2, 1]"));
    }

    [Fact]
    public void DoesNotNormalizeCallerControlledRuntimeProperties()
    {
        const string expected = """
            {
              "person_properties": {
                "$os": "Linux"
              }
            }
            """;
        const string actual = """
            {
              "person_properties": {
                "$os": "Windows"
              }
            }
            """;

        Assert.Throws<EqualException>(() => JsonAssert.EqualSnapshot(expected, actual));
    }
}

public class FinalWireSnapshots
{
    [Fact]
    public async Task MaximalFlagsRequestMatchesGolden()
    {
        var container = new TestContainer(services => services.Configure<PostHogOptions>(options =>
        {
            options.EvaluationContexts = ["checkout", "backend"];
        }));
        var requestHandler = container.FakeHttpMessageHandler.AddFlagsResponse("""{"flags": {}}""");
        var client = container.Activate<PostHogClient>();
        container.FakeTimeProvider.SetUtcNow(
            new DateTimeOffset(2024, 7, 8, 9, 10, 11, TimeSpan.Zero));
        var groups = new GroupCollection
        {
            new Group("company", "company-42", new Dictionary<string, object?>
            {
                ["plan"] = "enterprise",
                ["seats"] = 42
            }),
            new Group("project", "project-7", new Dictionary<string, object?>
            {
                ["region"] = "eu-west",
                ["archived"] = false
            })
        };

        await client.GetAllFeatureFlagsAsync(
            "person-123",
            new AllFeatureFlagsOptions
            {
                OnlyEvaluateLocally = false,
                PersonProperties = new Dictionary<string, object?>
                {
                    ["email"] = "person@example.com",
                    ["age"] = 37,
                    ["roles"] = new[] { "admin", "editor" },
                    ["nullable"] = null
                },
                Groups = groups,
                FlagKeysToEvaluate = ["checkout-redesign", "new-pricing"],
                DisableGeoIp = true
            });

        await AssertGoldenAsync("flags-request-maximal.json", requestHandler.GetReceivedRequestBody(indented: false));
    }

    [Fact]
    public async Task CompleteStacklessExceptionBatchMatchesGolden()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddBatchResponse();
        var client = container.Activate<PostHogClient>();
        container.FakeTimeProvider.SetUtcNow(
            new DateTimeOffset(2024, 7, 8, 9, 10, 11, TimeSpan.Zero));
        var timestamp = new DateTimeOffset(2024, 7, 8, 9, 10, 11, TimeSpan.FromHours(5.5));

        Assert.True(client.CaptureException(
            new InvalidOperationException("deterministic failure"),
            "exception-user",
            properties: null,
            groups: null,
            flags: null,
            timestamp));
        await client.FlushAsync();

        await AssertGoldenAsync("exception-batch-minimal.json", requestHandler.GetReceivedRequestBody(indented: false));
    }

    [Fact]
    public async Task AliasRequestMatchesGolden()
    {
        var container = new TestContainer();
        var requestHandler = container.FakeHttpMessageHandler.AddCaptureResponse();
        var client = container.Activate<PostHogClient>();
        container.FakeTimeProvider.SetUtcNow(
            new DateTimeOffset(2024, 7, 8, 9, 10, 11, TimeSpan.Zero));

        await client.AliasAsync("anonymous-session", "known-user", CancellationToken.None);

        await AssertGoldenAsync("alias-request.json", requestHandler.GetReceivedRequestBody(indented: false));
    }

    static async Task AssertGoldenAsync(string fileName, string actualJson)
    {
        var expectedJson = await File.ReadAllTextAsync(Path.Combine("Fixtures", "Snapshots", fileName));
        JsonAssert.EqualSnapshot(expectedJson, actualJson);
    }
}

public class LocalEvaluationSchemaSnapshots
{
    [Fact]
    public async Task PublicSemanticProjectionMatchesGolden()
    {
        var inboundJson = await File.ReadAllTextAsync(
            Path.Combine("Fixtures", "Snapshots", "local-evaluation-definitions-inbound.json"));
        var result = await JsonSerializerHelper.DeserializeFromCamelCaseJsonStringAsync<LocalEvaluationApiResult>(inboundJson);

        Assert.NotNull(result);
        var projection = Project(result);
        var expectedJson = await File.ReadAllTextAsync(
            Path.Combine("Fixtures", "Snapshots", "local-evaluation-definitions-projection.json"));
        JsonAssert.EqualSnapshot(expectedJson, JsonSerializer.Serialize(projection));
    }

    static Dictionary<string, object?> Project(LocalEvaluationApiResult result) => new()
    {
        ["flags"] = result.Flags.Select(flag => new Dictionary<string, object?>
        {
            ["id"] = flag.Id,
            ["team_id"] = flag.TeamId,
            ["name"] = flag.Name,
            ["key"] = flag.Key,
            ["filters"] = flag.Filters is null ? null : new Dictionary<string, object?>
            {
                ["groups"] = flag.Filters.Groups?.Select(group => new Dictionary<string, object?>
                {
                    ["variant"] = group.Variant,
                    ["properties"] = group.Properties?.Select(Project).ToArray(),
                    ["rollout_percentage"] = group.RolloutPercentage,
                    ["aggregation_group_type_index"] = group.AggregationGroupTypeIndex
                }).ToArray(),
                ["payloads"] = flag.Filters.Payloads?.ToDictionary(pair => pair.Key, pair => pair.Value),
                ["multivariate"] = flag.Filters.Multivariate is null ? null : new Dictionary<string, object?>
                {
                    ["variants"] = flag.Filters.Multivariate.Variants.Select(variant => new Dictionary<string, object?>
                    {
                        ["key"] = variant.Key,
                        ["name"] = variant.Name,
                        ["rollout_percentage"] = variant.RolloutPercentage
                    }).ToArray()
                },
                ["aggregation_group_type_index"] = flag.Filters.AggregationGroupTypeIndex,
                ["early_exit"] = flag.Filters.EarlyExit
            },
            ["deleted"] = flag.Deleted,
            ["active"] = flag.Active,
            ["ensure_experience_continuity"] = flag.EnsureExperienceContinuity,
            ["has_experiment"] = flag.HasExperiment
        }).ToArray(),
        ["group_type_mapping"] = result.GroupTypeMapping?.ToDictionary(pair => pair.Key, pair => pair.Value),
        ["cohorts"] = result.Cohorts?.ToDictionary(pair => pair.Key, pair => Project(pair.Value)),
        ["minimal_flag_called_events"] = result.MinimalFlagCalledEvents
    };

    static Dictionary<string, object?> Project(Filter filter) => filter switch
    {
        FilterSet set => new Dictionary<string, object?>
        {
            ["type"] = set.Type,
            ["values"] = set.Values.Select(Project).ToArray()
        },
        PropertyFilter property => Project(property),
        _ => throw new InvalidOperationException($"Unexpected filter type {filter.GetType().Name}.")
    };

    static Dictionary<string, object?> Project(PropertyFilter property) => new()
    {
        ["type"] = property.Type,
        ["key"] = property.Key,
        ["value"] = Project(property.Value),
        ["operator"] = property.Operator,
        ["group_type_index"] = property.GroupTypeIndex,
        ["negation"] = property.Negation,
        ["dependency_chain"] = property.DependencyChain?.ToArray()
    };

    static object? Project(PropertyFilterValue? value)
    {
        if (value?.ListOfStrings is not null)
        {
            return value.ListOfStrings.ToArray();
        }
        if (value?.StringValue is not null)
        {
            return value.StringValue;
        }
        if (value?.BooleanValue is not null)
        {
            return value.BooleanValue.Value;
        }
        return value?.CohortId;
    }
}
