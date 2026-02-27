#if NETSTANDARD2_0
// ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

internal static class DictionaryPolyfills
{
    public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        where TKey : notnull
    {
        if (dictionary.ContainsKey(key))
        {
            return false;
        }

        dictionary.Add(key, value);
        return true;
    }

    public static TValue? GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key)
        where TKey : notnull
    {
        return dictionary.TryGetValue(key, out var value) ? value : default;
    }

    public static TValue GetValueOrDefault<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> dictionary,
        TKey key,
        TValue defaultValue)
        where TKey : notnull
    {
        return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
    {
        key = pair.Key;
        value = pair.Value;
    }
}
#endif
