using System.Diagnostics.CodeAnalysis;
using PostHog.Library;

namespace PostHog;

using static Ensure;

/// <summary>
/// Represents a group and its properties.
/// </summary>
public record Group
{
    /// <summary>
    /// Constructs a <see cref="Group"/>
    /// </summary>
    public Group()
    {
        Properties = new Dictionary<string, object?>();
    }

    /// <summary>
    /// Constructs a <see cref="Group"/> with the specified properties.
    /// </summary>
    /// <param name="groupType">The type of group in PostHog. For example, company, project, etc.</param>
    /// <param name="groupKey">The identifier for the group such as the ID of the group in the database.</param>
    [SetsRequiredMembers]
    public Group(string groupType, string groupKey) : this()
    {
        GroupType = groupType;
        GroupKey = groupKey;
    }

    /// <summary>
    /// Constructs a <see cref="Group"/> with the specified properties.
    /// </summary>
    /// <param name="groupType">The type of group in PostHog. For example, company, project, etc.</param>
    /// <param name="groupKey">The identifier for the group such as the ID of the group in the database.</param>
    /// <param name="properties">The properties to associate with this group.</param>
    [SetsRequiredMembers]
    public Group(string groupType, string groupKey, IReadOnlyDictionary<string, object?> properties) : this(properties)
    {
        GroupType = groupType;
        GroupKey = groupKey;
    }

    /// <summary>
    /// Constructs a <see cref="Group"/> with the specified properties.
    /// </summary>
    /// <param name="properties"></param>
    public Group(IReadOnlyDictionary<string, object?> properties)
    {
        Properties = new Dictionary<string, object?>(properties);
    }

    /// <summary>
    /// The group properties to associate with this group. These can be used in feature flag calls to override what's on the server.
    /// </summary>
    public Dictionary<string, object?> Properties { get; }

    /// <summary>
    /// The type of group in PostHog. For example, company, project, etc.
    /// </summary>
    public required string GroupType { get; init; }

    /// <summary>
    /// The identifier for the group such as the ID of the group in the database.
    /// </summary>
    public required string GroupKey { get; init; }

    /// <summary>
    /// Adds a property and its value to the group.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="value">The value of the property.</param>
    /// <returns>The <see cref="Group"/> that contains these properties.</returns>
    public Group AddProperty(string name, object value)
    {
        NotNull(Properties).Add(name, value);
        return this;
    }

    /// <summary>
    /// The indexer for <see cref="Group"/> used to get or set a property value.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <exception cref="KeyNotFoundException"></exception>
    public object this[string name]
    {
        get => NotNull(Properties)[name] ?? throw new KeyNotFoundException();
        set => NotNull(Properties)[name] = value;
    }
}