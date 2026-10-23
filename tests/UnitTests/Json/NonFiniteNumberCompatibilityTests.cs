using System.Text.Json;
using System.Text.Json.Serialization;
using PostHog.Json;
using PostHog.Library;

namespace NonFiniteNumberCompatibilityTests;

public class TheSerializeMethod
{
    [Fact]
    public async Task FlattensExtensionDataOnNumberHandlingModels()
    {
        var model = new ExtensionDataModel
        {
            Extra = new Dictionary<string, object> { ["value"] = 1.25, ["nonFinite"] = double.NaN }
        };

        var json = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(model);

        JsonAssert.Equal("""{"value":1.25,"nonFinite":null}""", json);
    }

    [Fact]
    public async Task SkipsIgnoredMembersWithInvalidMetadata()
    {
        var json = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(new IgnoredMemberModel());

        JsonAssert.Equal("""{"value":1.25}""", json);
    }

    [Fact]
    public async Task SerializesPopulationCapableCollections()
    {
        var model = new PopulatedCollectionModel();
        model.Values.Add(1.25);
        model.Values.Add(double.NaN);

        var json = await JsonSerializerHelper.SerializeToCamelCaseJsonStringAsync(model);

        JsonAssert.Equal("""{"values":[1.25,null]}""", json);
    }

    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public sealed class ExtensionDataModel
    {
        [JsonExtensionData]
        public Dictionary<string, object> Extra { get; init; } = [];
    }

    public sealed class IgnoredMemberModel
    {
        public double Value { get; init; } = 1.25;

        [JsonIgnore]
        public CollidingNamesModel? Ignored { get; init; }
    }

    public sealed class CollidingNamesModel
    {
        [JsonPropertyName("name")]
        public string? First { get; init; }

        [JsonPropertyName("name")]
        public string? Second { get; init; }
    }

#pragma warning disable CA1002 // Exercise the built-in population-capable collection contract.
    public sealed class PopulatedCollectionModel
    {
        [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public List<double> Values { get; } = [];
    }
#pragma warning restore CA1002
}
