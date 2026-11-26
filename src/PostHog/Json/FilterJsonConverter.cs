using System.Text.Json;
using System.Text.Json.Serialization;
using PostHog.Api;
using static PostHog.Library.Ensure;
namespace PostHog.Json;

internal class FilterJsonConverter : JsonConverter<Filter>
{
    public override Filter? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var filterElement = document.RootElement;
        // Older definitions omit the type, or send null or an empty string, for a person filter.
        if (!filterElement.TryGetProperty("type", out var typeElement) || typeElement.ValueKind is JsonValueKind.Null)
        {
            return filterElement.Deserialize<PropertyFilter>(options);
        }

        if (typeElement.ValueKind is not JsonValueKind.String)
        {
            return new UnsupportedFilter(filterElement.Clone());
        }

        return typeElement.GetString() switch
        {
            "" or "person" or "group" or "cohort" or "flag" => filterElement.Deserialize<PropertyFilter>(options),
            "AND" or "OR" => filterElement.Deserialize<FilterSet>(options),
            _ => new UnsupportedFilter(filterElement.Clone())
        };
    }

    public override void Write(Utf8JsonWriter writer, Filter value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case UnsupportedFilter unsupportedFilter:
                unsupportedFilter.Definition.WriteTo(writer);
                break;
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