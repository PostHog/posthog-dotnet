using System.Text.Json;
using System.Text.Json.Nodes;

internal static class JsonAssert
{
    static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true
    };

    public static void Equal<T>(T expectedJson, JsonDocument? actualJson) =>
        Equal(JsonSerializer.SerializeToDocument(expectedJson, typeof(T)), actualJson);

    public static void Equal(string? expectedJson, JsonDocument? actualJson) =>
        Equal(expectedJson is null
            ? null
            : JsonNode.Parse(expectedJson), actualJson is null ? null : JsonNode.Parse(actualJson.RootElement.GetRawText()));

    public static void Equal(JsonDocument expectedJson, JsonDocument? actualJson) =>
        Equal(JsonNode.Parse(expectedJson.RootElement.GetRawText()), actualJson is null
            ? null
            : JsonNode.Parse(actualJson.RootElement.GetRawText()));

    public static void Equal(string expectedJson, string actualJson) =>
        Equal(JsonNode.Parse(expectedJson), JsonNode.Parse(actualJson));

    public static void Equal(string expectedJson, JsonNode? actualJson) =>
        Equal(JsonNode.Parse(expectedJson), actualJson);

    public static void Equal(JsonNode? expectedJson, JsonNode? actualJson)
    {
        // Convert back to string so we get helpful output if they're not equal.
        var expected = expectedJson?.ToJsonString(IndentedOptions);
        var actual = actualJson?.ToJsonString(IndentedOptions);
        Assert.Equal(expected, actual);
    }

    public static void Equal(
        IReadOnlyDictionary<string, JsonDocument> expectedJson,
        IReadOnlyDictionary<string, JsonDocument>? actualJson)
    {
        Assert.Equal(expectedJson.Keys, actualJson?.Keys);
        foreach (var key in expectedJson.Keys)
        {
            Equal(expectedJson[key], actualJson?[key]);
        }
    }
}