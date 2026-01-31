using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class JsonAssert
{
    // Match UUID format: "uuid": "8-4-4-4-12 hex characters"
    static readonly Regex UuidPattern = new(
        @"""uuid"":\s*""[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}""",
        RegexOptions.Compiled);

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

    /// <summary>
    /// Compares JSON strings after normalizing UUID fields to a placeholder value.
    /// This is useful for comparing JSON that contains randomly generated UUIDs.
    /// </summary>
    public static void EqualIgnoringUuids(string expectedJson, string actualJson)
    {
        // Normalize UUIDs in the actual JSON to match a placeholder
        var normalizedActual = NormalizeUuids(actualJson);
        Equal(JsonNode.Parse(expectedJson), JsonNode.Parse(normalizedActual));
    }

    /// <summary>
    /// Replaces all UUID values in JSON with a placeholder.
    /// </summary>
    static string NormalizeUuids(string json)
    {
        return UuidPattern.Replace(json, "\"uuid\": \"00000000-0000-0000-0000-000000000000\"");
    }
}