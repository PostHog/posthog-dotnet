using System.Text.Json;
using System.Text.Json.Serialization;
using PostHog.Json;
using PostHog.Library;

namespace PostHog.Api;

/// <summary>
/// The API Payload from the <c>/api/feature_flag/local_evaluation</c> endpoint used to evaluate feature flags
/// locally.
/// </summary>
internal record LocalEvaluationApiResult
{
    /// <summary>
    /// The list of feature flags.
    /// </summary>
    public required IReadOnlyList<LocalFeatureFlag> Flags { get; init; }

    /// <summary>
    /// Mappings of group IDs to group type.
    /// </summary>
    [JsonPropertyName("group_type_mapping")]
    public IReadOnlyDictionary<string, string>? GroupTypeMapping { get; init; }

    /// <summary>
    /// A mapping of cohort IDs to a set of filters.
    /// </summary>
    public IReadOnlyDictionary<string, FilterSet>? Cohorts { get; init; }

    /// <summary>
    /// Compares this instance to another <see cref="LocalEvaluationApiResult"/> for equality.
    /// </summary>
    /// <remarks>
    /// This is primarily used in unit tests to make it easy to compare expected with actual.
    /// </remarks>
    /// <param name="other">The other <see cref="LocalEvaluationApiResult"/> to compare with.</param>
    /// <returns><c>true</c> if they are equal, otherwise <c>false</c>.</returns>
    public virtual bool Equals(LocalEvaluationApiResult? other)
    {
        if (ReferenceEquals(other, null))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Flags.ListsAreEqual(other.Flags)
               && GroupTypeMapping.DictionariesAreEqual(other.GroupTypeMapping)
               && Cohorts.DictionariesAreEqual(other.Cohorts);
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    public override int GetHashCode() => HashCode.Combine(Flags, GroupTypeMapping, Cohorts);
}

/// <summary>
/// The specification of a feature flag.
/// </summary>
internal record LocalFeatureFlag
{
    /// <summary>
    /// The database identifier for the feature flag.
    /// </summary>
    public int Id { get; init; }

    [JsonPropertyName("team_id")]
    public int TeamId { get; init; }

    /// <summary>
    /// A human-friendly description of the feature flag.
    /// </summary>
    /// <remarks>
    /// In the PostHog UI, this is the description field.
    /// </remarks>
    public string? Name { get; init; }

    /// <summary>
    /// The key for the feature flag.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The set of filters that determine who sees what variant of the feature.
    /// </summary>
    public FeatureFlagFilters? Filters { get; init; }

    /// <summary>
    /// Whether the feature flag is deleted.
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// Whether the feature flag is active. If <c>false</c>, the feature flag is not evaluated.
    /// </summary>
    /// <remarks>
    /// On PostHog.com, this is the checkbox under "Status" labeled "Enabled".
    /// </remarks>
    public bool Active { get; init; } = true;

    /// <summary>
    /// Whether the feature flag has experience continuity enabled. This is not relevant for a server-side SDK.
    /// </summary>
    [JsonPropertyName("ensure_experience_continuity")]
    public bool EnsureExperienceContinuity { get; init; }
}

/// <summary>
/// Defines the targeting rules for a feature flag - essentially determining who sees what variant of the feature.
/// </summary>
/// <remarks>
/// In PostHog, this is stored as a JSON blob in the <c>posthog_featureflag</c> table.
/// </remarks>
internal record FeatureFlagFilters
{
    /// <summary>
    /// These are sets of conditions that determine who sees the feature flag. If any group matches, the flag is active
    /// for that user.
    /// </summary>
    public IReadOnlyList<FeatureFlagGroup>? Groups { get; init; }

    /// <summary>
    /// The payloads for the feature flag.
    /// </summary>
    /// <remarks>
    /// You may be tempted to change this type to <c>IReadonlyDictionary&lt;string, JsonDocument&gt;</c>, but that is
    /// incorrect. The payload value is a string <em>containing</em> JSON, not JSON itself. So it needs to be
    /// deserialized as a string, and then parsed as JSON. Ask me how I know.
    /// </remarks>
    [JsonConverter(typeof(ReadonlyDictionaryJsonConverter<string, string>))]
    public IReadOnlyDictionary<string, string>? Payloads { get; init; }

    /// <summary>
    /// The variants for the feature flag.
    /// </summary>
    public Multivariate? Multivariate { get; init; }

    /// <summary>
    /// The index of the aggregation group type. This is the Id of the group.
    /// </summary>
    [JsonPropertyName("aggregation_group_type_index")]
    public int? AggregationGroupTypeIndex { get; init; }

    /// <summary>
    /// Compares this instance to another <see cref="FeatureFlagFilters"/> for equality.
    /// </summary>
    /// <remarks>
    /// This is primarily used in unit tests to make it easy to compare expected with actual.
    /// </remarks>
    /// <param name="other">The other <see cref="FeatureFlagFilters"/> to compare with.</param>
    /// <returns><c>true</c> if they are equal, otherwise <c>false</c>.</returns>
    public virtual bool Equals(FeatureFlagFilters? other)
    {
        if (ReferenceEquals(other, null))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Groups.ListsAreEqual(other.Groups)
               && Payloads.DictionariesAreEqual(other.Payloads)
               && Multivariate == other.Multivariate
               && AggregationGroupTypeIndex == other.AggregationGroupTypeIndex;
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    public override int GetHashCode() => HashCode.Combine(Groups, Payloads, Multivariate, AggregationGroupTypeIndex);
}

/// <summary>
/// Set of conditions that determine who sees the feature flag. If any group matches, the flag is active for that user.
/// </summary>
internal record FeatureFlagGroup
{
    /// <summary>
    /// Conditions about the user/group. (e.g. "user is in country X" or "user is in cohort Y")
    /// </summary>
    public IReadOnlyList<PropertyFilter>? Properties { get; init; }

    /// <summary>
    /// Optional override to serve a specific variant to users matching this group.
    /// </summary>
    public string? Variant { get; init; }

    /// <summary>
    /// Optional percentage (0-100) for gradual rollouts. Defaults to 100.
    /// </summary>
    [JsonPropertyName("rollout_percentage")]
    public int? RolloutPercentage { get; init; } = 100;

    /// <summary>
    /// Compares this instance to another <see cref="FeatureFlagGroup"/> for equality.
    /// </summary>
    /// <param name="other">The other <see cref="FeatureFlagGroup"/> to compare with.</param>
    /// <returns><c>true</c> if they are equal, otherwise <c>false</c>.</returns>
    public virtual bool Equals(FeatureFlagGroup? other)
    {
        if (ReferenceEquals(other, null))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return ((Properties is null && other.Properties is null)
                || (Properties is not null && other.Properties is not null && Properties.SequenceEqual(other.Properties)))
               && Variant == other.Variant
               && RolloutPercentage == other.RolloutPercentage;
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    public override int GetHashCode() => HashCode.Combine(Properties, Variant, RolloutPercentage);
}

/// <summary>
/// Container for the variants of a multivariate feature flag.
/// </summary>
internal record Multivariate
{
    /// <summary>
    /// The set of variants.
    /// </summary>
    public required IReadOnlyCollection<Variant> Variants { get; init; }

    /// <summary>
    /// Compares this instance to another <see cref="Multivariate"/> for equality.
    /// </summary>
    /// <param name="other">The other <see cref="Multivariate"/> to compare with.</param>
    /// <returns><c>true</c> if they are equal, otherwise <c>false</c>.</returns>
    public virtual bool Equals(Multivariate? other)
    {
        if (ReferenceEquals(other, null))
        {
            return false;
        }

        return ReferenceEquals(this, other) || Variants.SequenceEqual(other.Variants);
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    public override int GetHashCode() => Variants.GetHashCode();
}

/// <summary>
/// A variant of a multivariate feature flag that can be served to users.
/// </summary>
internal record Variant
{
    /// <summary>
    /// The variant key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// A description of the variant.
    /// </summary>
    /// <remarks>
    /// On PostHog.com, this is the description field in the "Variants" section of the feature flag.
    /// </remarks>
    public string? Name { get; init; }

    /// <summary>
    /// The percentage of users this variant should be served to.
    /// </summary>
    [JsonPropertyName("rollout_percentage")]
    public double RolloutPercentage { get; init; } = 100;
}

/// <summary>
/// Base class for <see cref="FilterSet"/> or <see cref="PropertyFilter"/>.
/// </summary>
[JsonConverter(typeof(FilterJsonConverter))]
internal abstract record Filter
{
    /// <summary>
    /// The type of filter. For <see cref="FilterSet"/>, it'll be "OR" or "AND".
    /// For <see cref="PropertyFilter"/> it'll be "person" or "group".
    /// </summary>
    public FilterType Type { get; init; }
}

/// <summary>
/// A grouping ("AND" or "OR")
/// </summary>
internal record FilterSet : Filter
{
    /// <summary>
    /// The collection of filters to evaluate. Allows for nesting.
    /// </summary>
    public required IReadOnlyList<Filter> Values { get; init; }

    /// <summary>
    /// Compares this instance to another <see cref="FilterSet"/> for equality.
    /// </summary>
    /// <param name="other">The other <see cref="FilterSet"/> to compare with.</param>
    /// <returns><c>true</c> if they are equal, otherwise <c>false</c>.</returns>
    public virtual bool Equals(FilterSet? other)
    {
        if (ReferenceEquals(other, null))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Type == other.Type
               && Values.ListsAreEqual(other.Values);
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Values);
}

/// <summary>
/// A filter that filters on a property.
/// </summary>
internal record PropertyFilter : Filter
{
    /// <summary>
    /// The key of the property to filter on.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The value of the property to filter on.
    /// </summary>
    public required PropertyFilterValue Value { get; init; }

    /// <summary>
    /// The comparison operator to use for the filter.
    /// </summary>
    public ComparisonOperator? Operator { get; init; }

    /// <summary>
    /// The index of the group type.
    /// </summary>
    [JsonPropertyName("group_type_index")]
    public int? GroupTypeIndex { get; init; }

    /// <summary>
    /// Whether to negate the filter.
    /// </summary>
    public bool Negation { get; init; }

    /// <summary>
    /// Dependency chain for flag-type properties, representing flags that must be evaluated first.
    /// If empty or null, the flag has no dependencies or has circular dependencies.
    /// </summary>
    [JsonPropertyName("dependency_chain")]
    public IReadOnlyList<string>? DependencyChain { get; init; }

    /// <summary>
    /// Compares this instance to another <see cref="PropertyFilter"/> for equality.
    /// </summary>
    /// <param name="other">The other <see cref="PropertyFilter"/> to compare with.</param>
    /// <returns><c>true</c> if they are equal, otherwise <c>false</c>.</returns>
    public virtual bool Equals(PropertyFilter? other)
    {
        if (ReferenceEquals(other, null))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Type == other.Type
               && Key == other.Key
               && Value.Equals(other.Value)
               && Operator == other.Operator
               && GroupTypeIndex == other.GroupTypeIndex
               && Negation == other.Negation
               && (DependencyChain?.SequenceEqual(other.DependencyChain ?? []) ?? other.DependencyChain?.Any() != true);
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Key, Value, Operator, GroupTypeIndex, Negation);
}

/// <summary>
/// The types of comparison operators for a filter.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FilterType>))]
public enum FilterType
{
    /// <summary>
    /// Filters on person properties.
    /// </summary>
    [JsonPropertyName("person")]
    Person,

    /// <summary>
    /// Filters on group properties.
    /// </summary>
    [JsonPropertyName("group")]
    Group,

    /// <summary>
    /// Filters on cohort membership
    /// </summary>
    [JsonPropertyName("cohort")]
    Cohort,

    /// <summary>
    /// If any of the filters match, the group is considered a match.
    /// </summary>
    [JsonPropertyName("OR")]
    Or,

    /// <summary>
    /// If all of the filters match, the group is considered a match.
    /// </summary>
    [JsonPropertyName("AND")]
    And,

    /// <summary>
    /// Filters on how another flag was evaluated
    /// </summary>
    [JsonPropertyName("flag")]
    Flag
}
