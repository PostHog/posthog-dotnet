#if NETSTANDARD2_0
// ReSharper disable once CheckNamespace
namespace System;

internal static class StringPolyfills
{
    public static bool Contains(this string source, string value, StringComparison comparisonType)
    {
        return source.IndexOf(value, comparisonType) >= 0;
    }

    public static string Replace(this string source, string oldValue, string? newValue, StringComparison comparisonType)
    {
        if (comparisonType == StringComparison.Ordinal)
        {
            return source.Replace(oldValue, newValue ?? string.Empty);
        }

        var result = new Text.StringBuilder();
        var searchIndex = 0;

        while (searchIndex < source.Length)
        {
            var matchIndex = source.IndexOf(oldValue, searchIndex, comparisonType);
            if (matchIndex < 0)
            {
                result.Append(source, searchIndex, source.Length - searchIndex);
                break;
            }

            result.Append(source, searchIndex, matchIndex - searchIndex);
            result.Append(newValue);
            searchIndex = matchIndex + oldValue.Length;
        }

        return result.ToString();
    }

    public static int GetHashCode(this string source, StringComparison comparisonType)
    {
        return GetStringComparer(comparisonType).GetHashCode(source);
    }

    static StringComparer GetStringComparer(StringComparison comparisonType)
    {
        return comparisonType switch
        {
            StringComparison.CurrentCulture => StringComparer.CurrentCulture,
            StringComparison.CurrentCultureIgnoreCase => StringComparer.CurrentCultureIgnoreCase,
            StringComparison.InvariantCulture => StringComparer.InvariantCulture,
            StringComparison.InvariantCultureIgnoreCase => StringComparer.InvariantCultureIgnoreCase,
            StringComparison.Ordinal => StringComparer.Ordinal,
            StringComparison.OrdinalIgnoreCase => StringComparer.OrdinalIgnoreCase,
            _ => throw new ArgumentException($"Unknown StringComparison value: {comparisonType}", nameof(comparisonType))
        };
    }
}
#endif
