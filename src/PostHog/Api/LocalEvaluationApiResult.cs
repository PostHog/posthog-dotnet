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

    public LocalEvaluationApiResult()
    {
    }

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

    public override int GetHashCode() => HashCode.Combine(Flags, GroupTypeMapping, Cohorts);
}

/// <summary>
/// The specification of a feature flag.
/// </summary>
public record LocalFeatureFlag
{
    public int Id { get; init; }

    [JsonPropertyName("team_id")]
    public int TeamId { get; init; }

    public string? Name { get; init; }

    public required string Key { get; init; }

    public FeatureFlagFilters? Filters { get; init; }

    public bool Deleted { get; init; }

    public bool Active { get; init; } = true;

    [JsonPropertyName("ensure_experience_continuity")]
    public bool EnsureExperienceContinuity { get; init; } = false;
}

/// <summary>
/// Defines the targeting rules for a feature flag - essentially determining who sees what variant of the feature.
/// </summary>
/// <remarks>
/// In PostHog, this is stored as a JSON blob in the <c>posthog_featureflag</c> table.
/// </remarks>
public record FeatureFlagFilters
{
    /// <summary>
    /// These are sets of conditions that determine who sees the feature flag. If any group matches, the flag is active
    /// for that user.
    /// </summary>
    public IReadOnlyList<FeatureFlagGroup>? Groups { get; init; }

    /// <summary>
    /// The payloads for the feature flag.
    /// </summary>
    [JsonConverter(typeof(ReadonlyDictionaryJsonConverter<string, string>))]
    public IReadOnlyDictionary<string, string>? Payloads { get; init; }

    /// <summary>
    /// The variants for the feature flag.
    /// </summary>
    public Multivariate? Multivariate { get; init; }

    [JsonPropertyName("aggregation_group_type_index")]
    public int? AggregationGroupTypeIndex { get; init; }

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

    public override int GetHashCode() => HashCode.Combine(Groups, Payloads, Multivariate, AggregationGroupTypeIndex);
}

/// <summary>
/// Set of conditions that determine who sees the feature flag. If any group matches, the flag is active for that user.
/// </summary>
/// <param name="Variant">Optional override to serve a specific variant to users matching this group.</param>
/// <param name="Properties">Conditions about the user/group. (e.g. "user is in country X" or "user is in cohort Y")</param>
/// <param name="RolloutPercentage">Optional percentage (0-100) for gradual rollouts. Defaults to 100.</param>
public record FeatureFlagGroup(
    IReadOnlyList<PropertyFilter>? Properties,
    string? Variant = null,
    [property: JsonPropertyName("rollout_percentage")]
    int? RolloutPercentage = 100)
{
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

    public override int GetHashCode() => HashCode.Combine(Properties, Variant, RolloutPercentage);
}

public record Multivariate(IReadOnlyCollection<Variant> Variants)
{
    public virtual bool Equals(Multivariate? other)
    {
        if (ReferenceEquals(other, null))
        {
            return false;
        }

        return ReferenceEquals(this, other) || Variants.SequenceEqual(other.Variants);
    }

    public override int GetHashCode() => Variants.GetHashCode();
}

public record Variant(
    string Key,
    string Name,
    [property: JsonPropertyName("rollout_percentage")]
    double RolloutPercentage = 100);

/// <summary>
/// Base class for <see cref="FilterSet"/> or <see cref="PropertyFilter"/>.
/// </summary>
/// <param name="Type">
/// The type of filter. For <see cref="FilterSet"/>, it'll be "OR" or "AND".
/// For <see cref="PropertyFilter"/> it'll be "person" or "group".
/// </param>
[JsonConverter(typeof(FilterJsonConverter))]
public abstract record Filter(FilterType Type);

/// <summary>
/// A grouping ("AND" or "OR")
/// </summary>
/// <param name="Type">The type of filter. Either "AND" or "OR".</param>
/// <param name="Values">A collection of filters to evaluate. Allows for nesting.</param>
public record FilterSet(FilterType Type, IReadOnlyList<Filter> Values) : Filter(Type)
{
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

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Values);
}

/// <summary>
/// A filter that filters on a property.
/// </summary>
/// <param name="Type">The type of filter. Either "person" or "group".</param>
public record PropertyFilter(
    FilterType Type,
    string Key,
    PropertyFilterValue Value,
    ComparisonOperator? Operator = null,
    [property: JsonPropertyName("group_type_index")]
    int? GroupTypeIndex = null,
    bool Negation = false) : Filter(Type)
{
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
               && Negation == other.Negation;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(base.GetHashCode(), Key, Value, Operator, GroupTypeIndex, Negation);
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<FilterType>))]
public enum FilterType
{
    [JsonStringEnumMemberName("person")]
    Person,

    [JsonStringEnumMemberName("group")]
    Group,

    [JsonStringEnumMemberName("cohort")]
    Cohort,

    [JsonStringEnumMemberName("OR")]
    Or,

    [JsonStringEnumMemberName("AND")]
    And
}
