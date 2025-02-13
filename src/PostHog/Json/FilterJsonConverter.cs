using System.Text.Json;
using System.Text.Json.Serialization;
using PostHog.Api;
using static PostHog.Library.Ensure;
namespace PostHog.Json;

internal class FilterJsonConverter : JsonConverter<Filter>
{
    public override Filter? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var filterElement = JsonDocument.ParseValue(ref reader).RootElement;
        var type = filterElement.GetProperty("type").GetString();

        return type switch
        {
            "person" or "group" or "cohort" => filterElement.Deserialize<PropertyFilter>(options),
            "AND" or "OR" => filterElement.Deserialize<FilterSet>(options),
            _ => throw new InvalidOperationException($"Unexpected filter type: {type}")
        };
    }

    public override void Write(Utf8JsonWriter writer, Filter value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case PropertyFilter propertyFilter:
                JsonSerializer.Serialize(writer, propertyFilter, options);
                break;
            case FilterSet filterSet:
                JsonSerializer.Serialize(writer, filterSet, options);
                break;
            default:
                throw new InvalidOperationException($"Unexpected filter type: {NotNull(value).GetType().Name}");
        }
    }
}