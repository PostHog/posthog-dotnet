namespace PostHog.Features;

/// <summary>
/// The person and group property keys that a caller lists as known absent for one flag evaluation.
/// </summary>
internal sealed class KnownAbsentPropertyKeys
{
    /// <summary>
    /// The most keys each list can hold, counting duplicates. The <c>/flags</c> endpoint rejects longer lists.
    /// </summary>
    public const int MaxKeys = 1000;

    // Keys with this prefix name persons-table fields such as created_at, not person properties.
    const string PersonMetadataPrefix = "__posthog_person_metadata__";

    readonly Dictionary<string, HashSet<string>> _groups;

    KnownAbsentPropertyKeys(HashSet<string>? person, Dictionary<string, HashSet<string>> groups)
    {
        Person = person;
        _groups = groups;
    }

    /// <summary>
    /// The person property keys known absent, or <c>null</c> when none are listed.
    /// </summary>
    public HashSet<string>? Person { get; }

    /// <summary>
    /// Returns the keys known absent for the group type, or <c>null</c> when none are listed for it.
    /// </summary>
    public HashSet<string>? ForGroup(string groupType)
        => _groups.TryGetValue(groupType, out var keys) ? keys : null;

    /// <summary>
    /// Returns a copy that uses <paramref name="person"/> as the person keys, for a flag dependency that evaluates
    /// against the same properties as the condition that depends on it.
    /// </summary>
    public KnownAbsentPropertyKeys WithPerson(HashSet<string>? person)
        => ReferenceEquals(person, Person) ? this : new KnownAbsentPropertyKeys(person, _groups);

    /// <summary>
    /// Creates the known-absent keys from the evaluation options, or returns <c>null</c> when nothing is listed.
    /// </summary>
    public static KnownAbsentPropertyKeys? Create(AllFeatureFlagsOptions? options)
    {
        HashSet<string>? person = null;
        if (options?.PersonPropertyKeysKnownAbsent is { } personKeys)
        {
            person = new HashSet<string>(
                personKeys.Where(key => !key.StartsWith(PersonMetadataPrefix, StringComparison.Ordinal)),
                StringComparer.Ordinal);
            if (person.Count is 0)
            {
                person = null;
            }
        }

        var groups = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (groupType, keys) in options?.GroupPropertyKeysKnownAbsent ?? EmptyGroups)
        {
            if (keys is { Count: > 0 })
            {
                groups[groupType] = new HashSet<string>(keys, StringComparer.Ordinal);
            }
        }

        return person is null && groups.Count is 0 ? null : new KnownAbsentPropertyKeys(person, groups);
    }

    /// <summary>
    /// Returns <c>true</c> when the options list any person or group property key as known absent.
    /// </summary>
    public static bool HasAny(AllFeatureFlagsOptions? options)
        => options?.PersonPropertyKeysKnownAbsent is { Count: > 0 }
           || options?.GroupPropertyKeysKnownAbsent?.Values.Any(keys => keys is { Count: > 0 }) == true;

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> when a list holds more than <see cref="MaxKeys"/> keys.
    /// </summary>
    public static void Validate(AllFeatureFlagsOptions? options)
    {
        if (options?.PersonPropertyKeysKnownAbsent is { Count: > MaxKeys })
        {
            throw new ArgumentException(
                $"{nameof(AllFeatureFlagsOptions.PersonPropertyKeysKnownAbsent)} allows at most {MaxKeys} keys.",
                nameof(options));
        }

        foreach (var (groupType, keys) in options?.GroupPropertyKeysKnownAbsent ?? EmptyGroups)
        {
            if (keys is { Count: > MaxKeys })
            {
                throw new ArgumentException(
                    $"{nameof(AllFeatureFlagsOptions.GroupPropertyKeysKnownAbsent)} allows at most {MaxKeys} keys per group type, but '{groupType}' has {keys.Count}.",
                    nameof(options));
            }
        }
    }

    static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> EmptyGroups =
        new Dictionary<string, IReadOnlyCollection<string>>();
}
