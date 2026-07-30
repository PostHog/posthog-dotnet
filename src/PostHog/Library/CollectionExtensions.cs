using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading.Channels;

namespace PostHog.Library;

/// <summary>
/// Helpful extensions to collections.
/// </summary>
internal static class CollectionExtensions
{
    /// <summary>
    /// Wraps an <see cref="IEnumerable{T}"/> in a <see cref="IReadOnlyList{T}"/>.
    /// </summary>
    /// <param name="enumerable">The <see cref="IEnumerable{T}"/>.</param>
    /// <typeparam name="T">The type of objects to enumerate.</typeparam>
    /// <returns>The <see cref="IReadOnlyList{T}"/> that wraps the enumerable.</returns>
    public static IReadOnlyList<T> ToReadOnlyList<T>(this IEnumerable<T> enumerable)
    {
        return new ReadOnlyCollection<T>(enumerable.ToList());
    }

    /// <summary>
    /// Creates an <see cref="IReadOnlyDictionary{TKey,TValue}"/> from an <see cref="IEnumerable{T}"/> according to
    /// specified key selector and value selector functions.
    /// </summary>
    /// <param name="enumerable">The <see cref="IEnumerable{T}"/> to create a dictionary from.</param>
    /// <param name="keySelector">A function to extract a key from each element.</param>
    /// <param name="valueSelector">A function to extract a value from each element.</param>
    /// <typeparam name="TItem">The type of objects to enumerate.</typeparam>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <returns>The <see cref="IReadOnlyList{T}"/> that wraps the enumerable.</returns>
    public static IReadOnlyDictionary<TKey, TValue> ToReadOnlyDictionary<TKey, TValue, TItem>(
        this IEnumerable<TItem> enumerable,
        Func<TItem, TKey> keySelector,
        Func<TItem, TValue> valueSelector) where TKey : notnull
        => new ReadOnlyDictionary<TKey, TValue>(enumerable.ToDictionary(keySelector, valueSelector));

    /// <summary>
    /// Creates a shallow copy of a dictionary.
    /// </summary>
    /// <param name="dictionary">The dictionary to copy.</param>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <returns>A new dictionary containing the same keys and values.</returns>
    public static Dictionary<TKey, TValue> Copy<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> dictionary) where TKey : notnull
        => new(dictionary);

    /// <summary>
    /// Similar to Python's hash merging, this method merges the contents of one dictionary into another.
    /// The values of the other dictionary will overwrite the values of the original dictionary.
    /// </summary>
    /// <param name="dictionary">The source dictionary.</param>
    /// <param name="otherDictionary">The other dictionary.</param>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TObject">The value type.</typeparam>
    public static void Merge<TKey, TObject>(
        this IDictionary<TKey, TObject> dictionary,
        IReadOnlyDictionary<TKey, TObject> otherDictionary)
    {
        foreach (var (key, value) in otherDictionary)
        {
            dictionary[key] = value;
        }
    }

    /// <summary>
    /// Retrieves a value from a dictionary as the specified type or adds a new value if the key does not exist or
    /// if it exists, but is not the correct type.
    /// </summary>
    /// <param name="dictionary">The source dictionary to modify.</param>
    /// <param name="key">The key.</param>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <returns>The value or a new instance.</returns>
    public static TValue GetOrAdd<TKey, TValue>(
        this IDictionary<TKey, object> dictionary,
        TKey key) where TValue : new()
    {
        if (dictionary.TryGetValue(key, out var value) && value is TValue typedValue)
        {
            return typedValue;
        }

        var newValue = new TValue();
        dictionary[key] = newValue;
        return newValue;
    }

    /// <summary>
    /// Dequeues a batch of items from a <see cref="ConcurrentQueue{T}"/>.
    /// </summary>
    /// <param name="queue">The queue.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="items">The items to return.</param>
    /// <typeparam name="T">The type of the queue items.</typeparam>
    /// <returns><c>True</c> if any items are in the batch, otherwise <c>false</c></returns>
    public static bool TryReadBatch<T>(
        this ChannelReader<T> queue,
        int batchSize,
        out IReadOnlyCollection<T> items)
    {
        var batch = new List<T>(batchSize);
        while (batch.Count < batchSize && queue.TryRead(out var item))
        {
            batch.Add(item);
        }

        items = batch.Count > 0 ? batch : Array.Empty<T>();
        return batch.Count > 0;
    }

    internal static bool ListsAreEqual<T>(this IReadOnlyList<T>? list, IReadOnlyList<T>? otherList)
        => list is null && otherList is null
           || (list is not null
               && otherList is not null
               && list.SequenceEqual(otherList));

    internal static bool DictionariesAreEqual<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue>? dictionary,
        IReadOnlyDictionary<TKey, TValue>? otherDictionary)
        => dictionary is null && otherDictionary is null
           || (dictionary is not null
               && otherDictionary is not null
               && dictionary.Keys.SequenceEqual(otherDictionary.Keys)
               && dictionary.Values.SequenceEqual(otherDictionary.Values));
}