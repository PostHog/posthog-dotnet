using System.Text.Json;
using System.Text.Json.Nodes;
using PostHog.Json;

public static class JsonAssert
{
    static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true
    };

    public static void AreEqual(string expectedJson, JsonDocument actualJson) =>
        AreEqual(JsonNode.Parse(expectedJson), JsonNode.Parse(actualJson.RootElement.GetRawText()));

    public static void AreEqual(JsonDocument expectedJson, JsonDocument actualJson) =>
        AreEqual(JsonNode.Parse(expectedJson.RootElement.GetRawText()), JsonNode.Parse(actualJson.RootElement.GetRawText()));

    public static void AreEqual(string expectedJson, string actualJson) =>
        AreEqual(JsonNode.Parse(expectedJson), JsonNode.Parse(actualJson));

    public static void AreEqual(string expectedJson, JsonNode? actualJson) =>
        AreEqual(JsonNode.Parse(expectedJson), actualJson);

    public static void AreEqual(JsonNode? expectedJson, JsonNode? actualJson)
    {
        // Convert back to string so we get helpful output if they're not equal.
        var expected = expectedJson?.ToJsonString(IndentedOptions);
        var actual = actualJson?.ToJsonString(IndentedOptions);
        Assert.Equal(expected, actual);
    }
}