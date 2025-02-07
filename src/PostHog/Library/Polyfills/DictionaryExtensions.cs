#if NETSTANDARD2_0 || NETSTANDARD2_1
using System.Collections.ObjectModel;

namespace PostHog.Library.Polyfills;

internal static class DictionaryExtensions
{
    public static ReadOnlyDictionary<TKey, TValue> AsReadOnly<TKey, TValue>(this IDictionary<TKey, TValue> dictionary)
    {
        return new ReadOnlyDictionary<TKey, TValue>(dictionary);
    }
}
#endif