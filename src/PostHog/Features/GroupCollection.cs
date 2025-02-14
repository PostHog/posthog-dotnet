using System.Collections;
using System.Diagnostics.CodeAnalysis;
using static PostHog.Library.Ensure;

namespace PostHog;

/// <summary>
/// When calling an API that requires a group or set of groups, such as evaluating feature flags,
/// use this to specify the groups. This also provides a way to specify additional group properties which may be
/// required when doing local evaluation of feature flags.
/// </summary>
public class GroupCollection : ICollection<Group>
{
    readonly Dictionary<string, Group> _groups = new();

    /// <summary>
    /// Attempts to add the specified groupType and groupKey to this collection.
    /// </summary>
    /// <param name="groupType">The type of group in PostHog. For example, company, project, etc.</param>
    /// <param name="groupKey">The identifier for the group such as the ID of the group in the database.</param>
    /// <returns><c>true</c> if the group was added. <c>false</c> if the group type already exists.</returns>
    public bool TryAdd(string groupType, string groupKey) =>
        _groups.TryAdd(groupType, new Group { GroupType = groupType, GroupKey = groupKey });

    /// <summary>
    /// Adds a <see cref="Group"/> with the specified groupType and groupKey to the groups.
    /// </summary>
    /// <param name="groupType">The type of group in PostHog. For example, company, project, etc.</param>
    /// <param name="groupKey">The identifier for the group such as the ID of the group in the database.</param>
    /// <exception cref="ArgumentNullException">Thrown if a group with this group type already exists.</exception>
    public void Add(string groupType, string groupKey)
    {
        if (TryAdd(groupType, groupKey))
        {
            return;
        }
        ThrowArgumentExceptionIfGroupWithGroupTypeExists(groupType);
    }

    /// <summary>
    /// Attempts to add the specified group to this collection.
    /// </summary>
    /// <param name="group">The group to add.</param>
    /// <returns><c>true</c> if the group was added. <c>false</c> if the group type already exists.</returns>
    public bool TryAdd(Group group) => _groups.TryAdd(NotNull(group).GroupKey, group);

    /// <summary>
    /// Adds a <see cref="Group"/> to this collection.
    /// </summary>
    /// <param name="group">The group to add.</param>
    /// <exception cref="ArgumentNullException">Thrown if a group with this group type already exists.</exception>
    public void Add(Group group)
    {
        if (_groups.TryAdd(NotNull(group).GroupType, group))
        {
            return;
        }
        ThrowArgumentExceptionIfGroupWithGroupTypeExists(group.GroupKey);
    }

    /// <summary>
    /// Clears this collection.
    /// </summary>
    public void Clear() => _groups.Clear();

    /// <summary>
    /// Determines whether this collection contains the specified group type.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <returns><c>true</c> if a group with the same type exists. Otherwise <c>false</c>.</returns>
    public bool Contains(Group group) => Contains(NotNull(group).GroupType);

    /// <summary>
    /// Determines whether this collection contains the specified group type.
    /// </summary>
    /// <param name="groupType">The group type.</param>
    /// <returns><c>true</c> if a group with the same type exists. Otherwise <c>false</c>.</returns>
    public bool Contains(string groupType) => _groups.ContainsKey(groupType);

    /// <summary>
    /// Copies this collection to an array.
    /// </summary>
    /// <param name="array">The array to copy to.</param>
    /// <param name="arrayIndex">The index of the array at which copying begins.</param>
    public void CopyTo(Group[] array, int arrayIndex) => _groups.Values.CopyTo(array, arrayIndex);

    /// <summary>
    /// Removes a group from this collection based on its group type.
    /// </summary>
    /// <param name="group">The group to remove.</param>
    /// <returns><c>true</c> if the group was removed. <c>false</c> if the group does not exist.</returns>
    public bool Remove(Group group) => Remove(NotNull(group).GroupType);

    /// <summary>
    /// Removes a group from this collection based on its group type.
    /// </summary>
    /// <param name="groupType">The type of group to remove.</param>
    /// <returns><c>true</c> if the group was removed. <c>false</c> if the group does not exist.</returns>
    public bool Remove(string groupType) => _groups.Remove(groupType);

    /// <summary>
    /// The count of items in this collection.
    /// </summary>
    public int Count => _groups.Count;

    /// <summary>
    /// Whether or not this collection is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    static void ThrowArgumentExceptionIfGroupWithGroupTypeExists(string groupType) =>
        throw new ArgumentException($"A group with the `group_type` of '{groupType}' already exists.", nameof(groupType));


    public IEnumerator<Group> GetEnumerator() => _groups.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// The indexer for this <see cref="GroupCollection"/> used to get or set a group.
    /// </summary>
    /// <param name="groupType"></param>
    public Group this[string groupType]
    {
        get => _groups[groupType];
        set => _groups[groupType] = value;
    }

    /// <summary>
    /// Attempts to get a group by the specified group type.
    /// </summary>
    /// <param name="groupType">The group type.</param>
    /// <param name="group">The group and its properties.</param>
    /// <returns><c>true</c> if the group exists, otherwise false.</returns>
    public bool TryGetGroup(string groupType, [NotNullWhen(returnValue: true)] out Group? group)
        => _groups.TryGetValue(groupType, out group);
}