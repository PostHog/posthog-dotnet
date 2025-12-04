using System.Text.Json;
using System.Text.RegularExpressions;

namespace PostHog.AI.Utils;

public static class Sanitizer
{
    private const string RedactedImagePlaceholder = "[base64 image redacted]";
    private static readonly Regex DataUrlRegex = new(@"^data:([^;]+);base64,", RegexOptions.Compiled);
    // Conservative check: length > 20 and valid base64 chars
    private static readonly Regex RawBase64Regex = new(@"^[A-Za-z0-9+/]+=*$", RegexOptions.Compiled);

    public static object? Sanitize(object? input)
    {
        if (input == null) return null;

        if (input is string str)
        {
            return SanitizeString(str);
        }

        if (input is JsonElement jsonElement)
        {
            return SanitizeJsonElement(jsonElement);
        }

        if (input is IDictionary<string, object> dict)
        {
            var newDict = new Dictionary<string, object>();
            foreach (var kvp in dict)
            {
                newDict[kvp.Key] = Sanitize(kvp.Value)!;
            }
            return newDict;
        }

        if (input is IEnumerable<object> list)
        {
            return list.Select(Sanitize).ToList();
        }

        return input;
    }

    private static string SanitizeString(string str)
    {
        if (IsBase64DataUrl(str))
        {
            return RedactedImagePlaceholder;
        }

        if (IsRawBase64(str))
        {
            return RedactedImagePlaceholder;
        }

        return str;
    }

    private static object? SanitizeJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return SanitizeString(element.GetString()!);
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(SanitizeJsonElement).ToList();
            case JsonValueKind.Object:
                return element.EnumerateObject()
                    .ToDictionary(p => p.Name, p => SanitizeJsonElement(p.Value)!);
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                 if (element.TryGetInt32(out var i)) return i;
                 if (element.TryGetInt64(out var l)) return l;
                 return element.GetDouble();
            case JsonValueKind.Null:
                return null;
            default:
                return element.ToString();
        }
    }

    private static bool IsBase64DataUrl(string str)
    {
        return DataUrlRegex.IsMatch(str);
    }

    private static bool IsRawBase64(string str)
    {
        // Skip if it looks like a URL/path
        if (str.StartsWith("http", StringComparison.OrdinalIgnoreCase) || str.StartsWith('/') || str.StartsWith("./", StringComparison.OrdinalIgnoreCase) || str.StartsWith("../", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return str.Length > 20 && RawBase64Regex.IsMatch(str);
    }
}
