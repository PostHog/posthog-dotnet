using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
#if NETSTANDARD2_0 || NETSTANDARD2_1
using PostHog.Library.Polyfills;
using Convert = PostHog.Library.Polyfills.Convert;
#else
using Convert = System.Convert;
using System.Security.Cryptography;
#endif

using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PostHog.Api;
using PostHog.Exceptions;
using PostHog.Json;
using static PostHog.Library.Ensure;

namespace PostHog.Features;

/// <summary>
/// Class used to locally evaluate feature flags.
/// </summary>
internal sealed class LocalEvaluator
{
    readonly TimeProvider _timeProvider;
    readonly ILogger<LocalEvaluator> _logger;
    readonly ReadOnlyDictionary<string, LocalFeatureFlag> _localFeatureFlags;
    readonly ReadOnlyDictionary<long, FilterSet> _cohortFilters;
    readonly ReadOnlyDictionary<long, string> _groupTypeMapping;

    /// <summary>
    /// Constructs a <see cref="LocalEvaluator"/> with the specified flags.
    /// </summary>
    /// <param name="flags">The flags returned from the local evaluation endpoint.</param>
    /// <param name="timeProvider">The time provider <see cref="TimeProvider"/> to use to determine time.</param>
    /// <param name="logger">The logger</param>
    public LocalEvaluator(
        LocalEvaluationApiResult flags,
        TimeProvider timeProvider,
        ILogger<LocalEvaluator> logger)
    {
        LocalEvaluationApiResult = NotNull(flags);
        _timeProvider = timeProvider;
        _logger = logger;
        _cohortFilters = (LocalEvaluationApiResult.Cohorts ?? new Dictionary<string, FilterSet>())
            .Select(pair => (ConvertIdToInt64(pair.Key), pair.Value))
            .Where(pair => pair.Item1.HasValue)
            .ToDictionary(tuple => tuple.Item1.GetValueOrDefault(), tuple => tuple.Item2)
            .AsReadOnly();

        _localFeatureFlags = flags.Flags.ToDictionary(f => f.Key).AsReadOnly();
        _groupTypeMapping = (LocalEvaluationApiResult.GroupTypeMapping ?? new Dictionary<string, string>())
            .Select(pair => (ConvertIdToInt64(pair.Key), pair.Value))
            .Where(pair => pair.Item1.HasValue)
            .ToDictionary(tuple => tuple.Item1.GetValueOrDefault(), tuple => tuple.Item2)
            .AsReadOnly();
    }

    /// <summary>
    /// Tries to retrieve a <see cref="LocalFeatureFlag"/> with the specified key.
    /// </summary>
    /// <param name="key">The feature flag key.</param>
    /// <param name="flag">The local feature flag to return if it exists.</param>
    /// <returns><c>true</c> if it exists, otherwise <c>false</c>.</returns>
    public bool TryGetLocalFeatureFlag(string key, [NotNullWhen(returnValue: true)] out LocalFeatureFlag? flag)
        => _localFeatureFlags.TryGetValue(key, out flag);

    /// <summary>
    /// The flags returned from the API.
    /// </summary>
    public LocalEvaluationApiResult LocalEvaluationApiResult { get; }

    /// <summary>
    /// Constructs a <see cref="LocalEvaluator"/> with the specified flags.
    /// </summary>
    /// <param name="flags">The flags returned from the local evaluation endpoint.</param>
    public LocalEvaluator(LocalEvaluationApiResult flags) : this(
        flags,
        TimeProvider.System,
        NullLogger<LocalEvaluator>.Instance)
    {
    }

    /// <summary>
    /// Evaluates whether the specified feature flag matches the specified group and person properties.
    /// </summary>
    /// <remarks>
    /// In PostHog/posthog-python, this would be equivalent to <c>_compute_flag_locally</c>
    /// </remarks>
    /// <param name="key">The feature flag key.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="groups">Optional: Context of what groups are related to this event, example: { ["company"] = "id:5" }. Can be used to analyze companies instead of users.</param>
    /// <param name="personProperties">Optional: What person properties are known. Used to compute flags locally.</param>
    /// <param name="warnOnUnknownGroups">Whether to log a warning if the feature flag relies on a group type that's not in the supplied groups.</param>
    /// <returns></returns>
    public StringOrValue<bool> EvaluateFeatureFlag(
        string key,
        string distinctId,
        GroupCollection? groups = null,
        Dictionary<string, object?>? personProperties = null,
        bool warnOnUnknownGroups = true)
    {
        var flagToEvaluate = LocalEvaluationApiResult.Flags.SingleOrDefault(f => f.Key == key);
        if (flagToEvaluate is null)
        {
            throw new ArgumentException($"Flag {key} does not exist.", nameof(key));
        }

        return ComputeFlagLocally(
            flagToEvaluate,
            distinctId,
            groups: groups ?? [],
            personProperties ?? [],
            warnOnUnknownGroups);
    }

    public (IReadOnlyDictionary<string, FeatureFlag>, bool) EvaluateAllFlags(
        string distinctId,
        GroupCollection? groups = null,
        Dictionary<string, object?>? personProperties = null,
        bool warnOnUnknownGroups = true)
    {
        Dictionary<string, FeatureFlag> results = new();

        if (LocalEvaluationApiResult.Flags is [])
        {
            return (results, true);
        }

        var fallbackToDecide = false;

        foreach (var flag in LocalEvaluationApiResult.Flags)
        {
            try
            {
                var flagValue = ComputeFlagLocally(
                    flag,
                    distinctId,
                    groups ?? [],
                    personProperties ?? [],
                    warnOnUnknownGroups);

                results[flag.Key] = FeatureFlag.CreateFromLocalEvaluation(flag.Key, flagValue, flag);
            }
            catch (InconclusiveMatchException)
            {
                // No need to log this, since it's just telling us to fall back to `/decide`
                fallbackToDecide = true;
            }
            catch (Exception e) when (e is not ArgumentException and not NullReferenceException)
            {
                fallbackToDecide = true;
                _logger.LogErrorUnexpectedException(e);
            }
        }

        return (results, fallbackToDecide);
    }

    public StringOrValue<bool> ComputeFlagLocally(
        LocalFeatureFlag flag,
        string distinctId,
        GroupCollection groups,
        Dictionary<string, object?> personProperties,
        bool warnOnUnknownGroups = true)
    {
        return ComputeFlagLocallyWithCache(
            flag,
            distinctId,
            groups,
            personProperties,
            new Dictionary<string, StringOrValue<bool>>(),
            warnOnUnknownGroups);
    }

    StringOrValue<bool> ComputeFlagLocallyWithCache(
        LocalFeatureFlag flag,
        string distinctId,
        GroupCollection groups,
        Dictionary<string, object?> personProperties,
        Dictionary<string, StringOrValue<bool>> evaluationCache,
        bool warnOnUnknownGroups = true)
    {
        // Check if we've already evaluated this flag to avoid infinite recursion
        if (evaluationCache.TryGetValue(flag.Key, out var cachedResult))
        {
            return cachedResult;
        }

        if (flag.EnsureExperienceContinuity)
        {
            throw new InconclusiveMatchException($"Flag \"{flag.Key}\" has experience continuity enabled");
        }

        if (!flag.Active)
        {
            var result = false;
            evaluationCache[flag.Key] = result;
            return result;
        }


        var filters = flag.Filters;
        var aggregationGroupIndex = filters?.AggregationGroupTypeIndex;

        StringOrValue<bool> flagResult;
        if (!aggregationGroupIndex.HasValue)
        {
            flagResult = MatchFeatureFlagProperties(
                flag,
                distinctId,
                personProperties,
                evaluationCache,
                groups);
        }
        else
        {
            if (!_groupTypeMapping.TryGetValue(aggregationGroupIndex.Value, out var groupType))
            {
                // Weird: We have a group type index that doesn't point to an actual group.
                _logger.LogWarnUnknownGroupType(aggregationGroupIndex.Value, flag.Key);
                throw new InconclusiveMatchException($"Flag has unknown group type index: {aggregationGroupIndex}");
            }

            if (groups.TryGetGroup(groupType, out var group))
            {
                flagResult = MatchFeatureFlagProperties(
                    flag,
                    group.GroupKey,
                    group.Properties,
                    evaluationCache,
                    groups);
            }
            else
            {
                // Don't failover to `/flags`, since response will be the same
                if (warnOnUnknownGroups)
                {
                    _logger.LogWarnGroupTypeNotPassedIn(flag.Key);
                }
                else
                {
                    _logger.LogDebugGroupTypeNotPassedIn(flag.Key);
                }

                flagResult = false;
            }
        }

        evaluationCache[flag.Key] = flagResult;
        return flagResult;
    }

    StringOrValue<bool> MatchFeatureFlagProperties(
        LocalFeatureFlag flag,
        string distinctId,
        Dictionary<string, object?>? properties, /* person or group properties */
        Dictionary<string, StringOrValue<bool>> evaluationCache,
        GroupCollection? groups = null)
    {
        var filters = flag.Filters;
        var flagConditions = filters?.Groups ?? [];
        var isInconclusive = false;
        var flagVariants = filters?.Multivariate?.Variants ?? [];

        foreach (var condition in flagConditions)
        {
            try
            {
                // if any one condition resolves to True, we can short circuit and return
                // the matching variant
                if (!IsConditionMatch(flag, distinctId, condition, properties, evaluationCache, groups))
                {
                    continue;
                }

                var variantOverride = condition.Variant;
                var variant = variantOverride is not null
                              && flagVariants.Select(v => v.Key).Contains(variantOverride)
                    ? variantOverride
                    : GetMatchingVariant(flag, distinctId);

                return variant is not null
                    ? new StringOrValue<bool>(variant)
                    : true;
            }
            catch (RequiresServerEvaluationException)
            {
                // Static cohort or other missing server-side data - must fallback to API
                throw;
            }
            catch (InconclusiveMatchException)
            {
                // Evaluation error (bad regex, invalid date, missing property, etc.)
                // Track that we had an inconclusive match, but try other conditions
                isInconclusive = true;
            }
        }

        if (isInconclusive)
        {
            throw new InconclusiveMatchException("Can't determine if feature flag is enabled or not with given properties");
        }

        // We can only return False when either all conditions are False, or
        // no condition was inconclusive.
        return false;
    }

    bool IsConditionMatch(
        LocalFeatureFlag flag,
        string distinctId,
        FeatureFlagGroup condition,
        Dictionary<string, object?>? properties,
        Dictionary<string, StringOrValue<bool>> evaluationCache,
        GroupCollection? groups = null)
    {
        var rolloutPercentage = condition.RolloutPercentage;
        if (condition.Properties is not null)
        {
            if (condition.Properties.Select(property => property.Type switch
                {
                    FilterType.Cohort => MatchCohort(property, distinctId, properties),
                    FilterType.Flag => MatchFlagDependencyFilter(property, distinctId, properties, evaluationCache, groups),
                    _ => MatchProperty(property, distinctId, properties)
                }).Any(isMatch => !isMatch))
            {
                return false;
            }
        }

        if (rolloutPercentage is 100)
        {
            return true;
        }

        var hashValue = Hash(flag.Key, distinctId);
        return !(hashValue > rolloutPercentage / 100.0);
    }

    static string? GetMatchingVariant(LocalFeatureFlag flag, string distinctId)
    {
        var hashValue = Hash(flag.Key, distinctId, salt: "variant");
        return CreateVariantLookupTable(flag)
            .FirstOrDefault(variant => hashValue >= variant.MinValue && hashValue < variant.MaxValue)
            ?.Key;
    }

    record VariantRange(string Key, double MinValue, double MaxValue);

    static List<VariantRange> CreateVariantLookupTable(LocalFeatureFlag flag)
    {
        List<VariantRange> results = [];
        var multivariateVariants = flag.Filters?.Multivariate?.Variants;
        if (multivariateVariants is null)
        {
            return results;
        }
        double minValue = 0;
        foreach (var variant in multivariateVariants)
        {
            var maxValue = minValue + variant.RolloutPercentage / 100.0;
            results.Add(new VariantRange(variant.Key, minValue, maxValue));
            minValue = maxValue;
        }

        return results;
    }

    bool MatchCohort(
        PropertyFilter filter,
        string distinctId,
        Dictionary<string, object?>? propertyValues)
    {
        // Cohort properties are in the form of property groups like this:
        // {
        //     "cohort_id": {
        //         "type": "AND|OR",
        //         "values": [{
        //            "key": "property_name", "value": "property_value"
        //        }]
        //     }
        // }
        var cohortId = filter.Value.CohortId;
        if (cohortId is null || !_cohortFilters.TryGetValue(cohortId.Value, out var conditions))
        {
            throw new RequiresServerEvaluationException($"cohort {cohortId} not found in local cohorts - likely a static cohort that requires server evaluation");
        }

        return MatchPropertyGroup(conditions, distinctId, propertyValues);
    }

    bool MatchPropertyGroup(FilterSet? filterSet, string distinctId, Dictionary<string, object?>? propertyValues)
    {
        if (filterSet is null)
        {
            return true;
        }

        var filters = filterSet.Values;
        if (filters is null or [])
        {
            // Empty groups are no-ops, always match
            return true;
        }

        bool errorMatchingLocally = false;

        // Test the first element to see what type of filters we're dealing with here.
        var isFilterSet = filters[0] is FilterSet;

        if (isFilterSet)
        {
            // A nested property group.
            // We expect every filter to be a filter set. At least this is how the other client libraries work.
            foreach (var filter in filters)
            {
                if (filter is not FilterSet childFilterSet)
                {
                    continue;
                }

                try
                {
                    var isMatch = MatchPropertyGroup(childFilterSet, distinctId, propertyValues);
                    if (childFilterSet.Type is FilterType.And)
                    {
                        if (!isMatch)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        // OR group
                        if (isMatch)
                        {
                            return true;
                        }
                    }
                }
                catch (InconclusiveMatchException e)
                {
                    _logger.LogDebugFailedToComputeProperty(e, filter);
                    errorMatchingLocally = true;
                }
            }

            if (errorMatchingLocally)
            {
                throw new InconclusiveMatchException("Can't match cohort without a given cohort property value");
            }

            // if we get here, all matched in AND case, or none matched in OR case
            return filterSet.Type is FilterType.And;
        }

        foreach (var filter in filters)
        {
            if (filter is not PropertyFilter propertyFilter)
            {
                continue;
            }
            try
            {
                var isMatch = filter.Type is FilterType.Cohort
                    ? MatchCohort(propertyFilter, distinctId, propertyValues)
                    : MatchProperty(propertyFilter, distinctId, propertyValues);
                var negation = propertyFilter.Negation;

                if (filterSet.Type is FilterType.And)
                {
                    switch (isMatch)
                    {
                        case false when !negation:
                        case true when negation:
                            // If negated property, do the inverse
                            return false;
                    }
                }
                else // OR Group
                {
                    switch (isMatch)
                    {
                        case true when !negation:
                        case false when negation:
                            return true;
                    }
                }

            }
            catch (InconclusiveMatchException e)
            {
                _logger.LogDebugFailedToComputeProperty(e, filter);
                errorMatchingLocally = true;
            }
        }

        if (errorMatchingLocally)
        {
            throw new InconclusiveMatchException("Can't match cohort without a given cohort property value");
        }

        // if we get here, all matched in AND case, or none matched in OR case
        return filterSet.Type is FilterType.And;
    }

    /// <summary>
    /// Evaluates a feature flag for a given set of properties.
    /// </summary>
    /// <remarks>
    /// Only looks for matches where the key exists in properties.
    /// Doesn't support the operator <c>is_not_set</c>.
    /// </remarks>
    /// <param name="propertyFilter">The <see cref="PropertyFilter"/> to evaluate.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="properties">The overriden values that describe the user/group.</param>
    /// <returns><c>true</c> if the current user/group matches the property. Otherwise <c>false</c>.</returns>
    bool MatchProperty(PropertyFilter propertyFilter, string distinctId, Dictionary<string, object?>? properties)
    {
        // Handle flag dependencies
        if (propertyFilter.Type is FilterType.Flag)
        {
            // Flag dependencies can't be evaluated from this context, throw inconclusive
            throw new InconclusiveMatchException($"Flag dependency '{propertyFilter.Key}' cannot be evaluated without evaluation context");
        }

        if (propertyFilter.Operator is ComparisonOperator.IsNotSet)
        {
            throw new InconclusiveMatchException("Can't match properties with operator is_not_set");
        }

        var key = NotNull(propertyFilter.Key);
        if (propertyFilter.Value is not { } propertyValue)
        {
            throw new InconclusiveMatchException("The filter property value is null");
        }

        var value = propertyValue ?? throw new InconclusiveMatchException("The filter property value is null");

        // The overrideValue is the value that the user or group has set for the property. It's called "override value"
        // because when passing it to the `/decide` endpoint, it overrides the values stored in PostHog. For local
        // evaluation, it's a bit of a misnomer because this is the *only* value we're concerned with. I thought about
        // naming this to comparand but wanted to keep the naming consistent with the other client libraries.
        // @haacked
        object? overrideValue;

        // distinct_id is a special property that should be available but not necessarily present in properties.
        if (string.Equals(key, PostHogProperties.DistinctId, StringComparison.Ordinal))
        {
            overrideValue = distinctId;
        }
        // Check all remaining properties.
        else if (NotNull(properties).TryGetValue(key, out var propValue))
        {
            overrideValue = propValue;
        }
        else
        {
            throw new InconclusiveMatchException("Can't match properties without a given property value");
        }

        if (overrideValue is null && propertyFilter.Operator != ComparisonOperator.IsNot)
        {
            // If the value is null, just fail the feature flag comparison. This doesn't throw an
            // InconclusiveMatchException because the property value was provided.
            return false;
        }

        return propertyFilter.Operator switch
        {
            ComparisonOperator.Exact => value.IsExactMatch(overrideValue),
            ComparisonOperator.IsNot => !value.IsExactMatch(overrideValue),
            ComparisonOperator.GreaterThan => value < overrideValue,
            ComparisonOperator.GreaterThanOrEquals => value <= overrideValue,
            ComparisonOperator.LessThan => value > overrideValue,
            ComparisonOperator.LessThanOrEquals => value >= overrideValue,
            ComparisonOperator.ContainsIgnoreCase => value.IsContainedBy(overrideValue, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.DoesNotContainIgnoreCase => !value.IsContainedBy(overrideValue, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.Regex => value.IsRegexMatch(overrideValue),
            ComparisonOperator.NotRegex => !value.IsRegexMatch(overrideValue),
            ComparisonOperator.IsSet => true, // We already checked to see that the key exists.
            ComparisonOperator.IsDateBefore => value.IsDateBefore(overrideValue, _timeProvider.GetUtcNow()),
            ComparisonOperator.IsDateAfter => !value.IsDateBefore(overrideValue, _timeProvider.GetUtcNow()),
            null => true, // If no operator is specified, just return true.
            _ => throw new InconclusiveMatchException($"Unknown operator: {propertyFilter.Operator}")
        };
    }

    /// <summary>
    /// Evaluates a flag dependency property filter.
    /// </summary>
    /// <param name="propertyFilter">The <see cref="PropertyFilter"/> to evaluate.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="properties">The overridden values that describe the user/group.</param>
    /// <param name="evaluationCache">The cache of already evaluated flags.</param>
    /// <param name="groups">Optional: Context of what groups are related to this event, example: { ["company"] = "id:5" }. Can be used to analyze companies instead of users.</param>
    /// <returns><c>true</c> if the current user/group matches the flag dependency. Otherwise <c>false</c>.</returns>
    bool MatchFlagDependencyFilter(
        PropertyFilter propertyFilter,
        string distinctId,
        Dictionary<string, object?>? properties,
        Dictionary<string, StringOrValue<bool>> evaluationCache,
        GroupCollection? groups = null)
    {
        Debug.Assert(propertyFilter.Type == FilterType.Flag);

        // Validate inputs and dependencies
        var (flagKey, propertyValue) = ValidateFlagDependencyFilter(propertyFilter);

        // Evaluate all dependencies in the chain and return the value of the last one
        EvaluateDependencyChain(propertyFilter.DependencyChain!, distinctId, properties, evaluationCache, groups);

        // Get the evaluated value for this flag
        var immediateDependencyValue = evaluationCache.TryGetValue(flagKey, out var keyForFlagDependencyFilter)
            ? keyForFlagDependencyFilter
            : throw new InconclusiveMatchException($"Flag dependency '{flagKey}' is missing in evaluation cache");

        // Match the value against expectations
        return MatchesDependencyValue(propertyValue, immediateDependencyValue);
    }

    static (string flagKey, PropertyFilterValue propertyValue) ValidateFlagDependencyFilter(PropertyFilter propertyFilter)
    {
        var flagKey = NotNull(propertyFilter.Key);

        if (propertyFilter.Value is not { } propertyValue)
        {
            throw new InconclusiveMatchException("The filter property value is null");
        }

        // Check if dependency_chain is present - it should always be provided for flag dependencies
        if (propertyFilter.DependencyChain is null)
        {
            throw new InconclusiveMatchException($"Flag dependency property for '{flagKey}' is missing required 'dependency_chain' field");
        }

        // Handle circular dependency (empty chain means circular)
        if (propertyFilter.DependencyChain.Count == 0)
        {
            // Circular dependencies should evaluate to inconclusive as per PostHog API design
            throw new InconclusiveMatchException($"Flag dependency property for '{flagKey}' has an empty 'dependency_chain' field indicating a circular dependency");
        }

        // The last item in the dependency chain should be the value in the current property filter.
        if (propertyFilter.DependencyChain[^1] != flagKey)
        {
            throw new InconclusiveMatchException($"Flag dependency property for '{flagKey}' has an invalid 'dependency_chain' field - last item should be the flag key itself");
        }

        return (flagKey, propertyValue);
    }

    // Evaluates every flag in the dependency chain in order and returns the result of the last one.
    void EvaluateDependencyChain(IReadOnlyList<string> dependencyChain,
        string distinctId,
        Dictionary<string, object?>? properties,
        Dictionary<string, StringOrValue<bool>> evaluationCache,
        GroupCollection? groups)
    {
        // Evaluate all dependencies in the chain order and cache the results.
        foreach (var depFlagKey in dependencyChain)
        {
            if (evaluationCache.ContainsKey(depFlagKey))
            {
                continue;
            }

            evaluationCache[depFlagKey] = EvaluateSingleDependency(depFlagKey, distinctId, properties, evaluationCache, groups);
        }
    }

    // Evaluates a single flag dependency and returns the result.
    StringOrValue<bool> EvaluateSingleDependency(
        string depFlagKey,
        string distinctId,
        Dictionary<string, object?>? properties,
        Dictionary<string, StringOrValue<bool>> evaluationCache,
        GroupCollection? groups)
    {
        // Need to evaluate this dependency first
        if (!_localFeatureFlags.TryGetValue(depFlagKey, out var depFlag))
        {
            throw new InconclusiveMatchException($"Cannot evaluate flag dependency '{depFlagKey}' - flag not found in local flags");
        }

        // Check if the dependency flag is active (same check as in ComputeFlagLocallyWithCache)
        if (!depFlag.Active)
        {
            return false;
        }

        // Recursively evaluate the dependency
        try
        {
            var depResult = ComputeFlagLocallyWithCache(
                depFlag,
                distinctId,
                groups ?? [],
                properties ?? new Dictionary<string, object?>(),
                evaluationCache,
                false);
            return depResult;
        }
        catch (InconclusiveMatchException e)
        {
            // If we can't evaluate a dependency, propagate the error
            throw new InconclusiveMatchException($"Cannot evaluate flag dependency '{depFlagKey}': {e.Message}", e);
        }
    }

    internal static bool MatchesDependencyValue(PropertyFilterValue expectedValue, StringOrValue<bool> actualValue)
    {
        return actualValue switch
        {
            // String variant case - check for exact match or boolean true
            { IsString: true, StringValue: { Length: > 0 } stringValue } =>
                expectedValue switch
                {
                    { BooleanValue: { } booleanValue } => booleanValue, // Any variant matches boolean true
                    { StringValue: { } expectedString } => string.Equals(stringValue, expectedString, StringComparison.Ordinal),
                    _ => false
                },

            // Boolean case - must match expected boolean value
            { IsValue: true, Value: { } boolValue } when expectedValue.BooleanValue.HasValue =>
                expectedValue.BooleanValue.Value == boolValue,

            _ => false
        };
    }

    long? ConvertIdToInt64(string id)
    {
        if (long.TryParse(id, out var intId))
        {
            return intId;
        }

        _logger.LogErrorInvalidGroupIdSkipped(id);
        return null;
    }

    static byte[] HashData(string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
#pragma warning disable CA5350 // This SHA is not used for security purposes
#if NETSTANDARD2_0 || NETSTANDARD2_1
        using var sha1 = new System.Security.Cryptography.SHA1Managed();
        return sha1.ComputeHash(keyBytes);
#else
        return SHA1.HashData(keyBytes);
#endif
#pragma warning restore
    }

    // This function takes a distinct_id and a feature flag key and returns a float between 0 and 1.
    // Given the same distinct_id and key, it'll always return the same float. These floats are
    // uniformly distributed between 0 and 1, so if we want to show this feature to 20% of traffic
    // we can do _hash(key, distinct_id) < 0.2
    // Ported from https://github.com/PostHog/posthog-python/blob/master/posthog/feature_flags.py#L23C1-L30
    static double Hash(string key, string distinctId, string salt = "")
    {
        var hashBytes = HashData($"{key}.{distinctId}{salt}");

#pragma warning restore CA5350

        // Convert the first 15 characters of the hex representation to an integer
        var hexString = Convert.ToHexString(hashBytes)[..15];
        var hashVal = Convert.ToUInt64(hexString, 16);

        // Ensure the value is within the correct range (60 bits)
        hashVal &= 0xFFFFFFFFFFFFFFF;

        return hashVal / LongScale;
    }

    const double LongScale = 0xFFFFFFFFFFFFFFF;
}

internal static partial class LocalEvaluatorLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "[FEATURE FLAGS] Unknown group type index {AggregationGroupIndex} for feature flag {FlagKey}")]
    public static partial void LogWarnUnknownGroupType(this ILogger<LocalEvaluator> logger, int aggregationGroupIndex, string flagKey);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "[FEATURE FLAGS] Can't compute group feature flag: {FlagKey} without group types passed in")]
    public static partial void LogDebugGroupTypeNotPassedIn(this ILogger<LocalEvaluator> logger, string flagKey);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "[FEATURE FLAGS] Can't compute group feature flag: {FlagKey} without group types passed in")]
    public static partial void LogWarnGroupTypeNotPassedIn(this ILogger<LocalEvaluator> logger, string flagKey);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Debug,
        Message = "Failed to compute property {Property} locally")]
    public static partial void LogDebugFailedToComputeProperty(this ILogger<LocalEvaluator> logger, Exception e, Filter property);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "Group Type mapping has an invalid group type id: {GroupTypeId}. Skipping it.")]
    public static partial void LogErrorInvalidGroupIdSkipped(this ILogger<LocalEvaluator> logger, string groupTypeId);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Error,
        Message = "[FEATURE FLAGS] Unable to get feature flags and payloads")]
    public static partial void LogErrorUnableToGetFeatureFlagsAndPayloads(this ILogger<LocalEvaluator> logger, Exception exception);

    [LoggerMessage(
        EventId = 500,
        Level = LogLevel.Error,
        Message = "Unexpected exception occurred during local evaluation.")]
    public static partial void LogErrorUnexpectedException(this ILogger<LocalEvaluator> logger, Exception exception);
}