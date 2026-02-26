#if NETSTANDARD2_0
// ReSharper disable once CheckNamespace
namespace System;

internal static class StringPolyfills
{
    public static int IndexOf(this string source, char value, StringComparison comparisonType)
    {
        return source.IndexOf(value.ToString(), comparisonType);
    }
}
#endif
