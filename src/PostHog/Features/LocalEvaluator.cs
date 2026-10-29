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
using PostHog.Library;
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

    const double LongScale = 0xFFFFFFFFFFFFFFF;

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
    /// Whether the server gated this project into sending minimal <c>$feature_flag_called</c> events,
    /// as reported by the local evaluation payload. <c>false</c> when the payload does not carry the field.
    /// </summary>
    public bool MinimalFlagCalledEvents => LocalEvaluationApiResult.MinimalFlagCalledEvents == true;

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

    /// <summary>
    /// Evaluates every flag in scope.
    /// </summary>
    /// <returns>
    /// The resolved flags, whether a remote fallback is needed, and the reason for each flag in scope that local
    /// evaluation left unresolved.
    /// </returns>
    public (IReadOnlyDictionary<string, FeatureFlag>, bool, IReadOnlyDictionary<string, UnresolvedFlagReason>) EvaluateAllFlags(
        string distinctId,
        GroupCollection? groups = null,
        Dictionary<string, object?>? personProperties = null,
        bool warnOnUnknownGroups = true,
        IReadOnlyCollection<string>? flagKeysToEvaluate = null)
    {
        Dictionary<string, FeatureFlag> results = new();
        Dictionary<string, UnresolvedFlagReason> unresolved = new(StringComparer.Ordinal);

        if (LocalEvaluationApiResult.Flags is [])
        {
            return (results, true, unresolved);
        }

        var fallbackToRemote = false;
        IEnumerable<LocalFeatureFlag> flagsToEvaluate = LocalEvaluationApiResult.Flags;
        if (flagKeysToEvaluate is { Count: > 0 })
        {
            var requestedFlagKeys = new HashSet<string>(flagKeysToEvaluate, StringComparer.Ordinal);
            fallbackToRemote = requestedFlagKeys.Any(key => !_localFeatureFlags.ContainsKey(key));
            flagsToEvaluate = flagsToEvaluate.Where(flag => requestedFlagKeys.Contains(flag.Key));
        }

        foreach (var flag in flagsToEvaluate)
        {
            try
            {
                var flagValue = ComputeFlagLocally(
                    flag,
                    distinctId,
                    groups ?? [],
                    personProperties ?? [],
                    warnOnUnknownGroups);

                results[flag.Key] = FeatureFlag.CreateFromLocalEvaluation(flag.Key, flagValue, flag, _logger);
            }
            catch (InconclusiveMatchException e)
            {
                // No need to log this, since it's just telling us to fall back to the `/flags` endpoint.
                fallbackToRemote = true;
                unresolved[flag.Key] = e.Reason;
            }
            catch (RequiresServerEvaluationException e)
            {
                // Same as above. The load-time warning already names the static cohort that causes this.
                fallbackToRemote = true;
                unresolved[flag.Key] = e.Reason;
            }
            catch (Exception e) when (e is not ArgumentException and not NullReferenceException)
            {
                fallbackToRemote = true;
                _logger.LogErrorUnexpectedException(e);
            }
        }

        return (results, fallbackToRemote, unresolved);
    }

    /// <summary>
    /// Returns the unresolved reason that comes from the flag's definition alone, so it is known when definitions
    /// load. Matches the reason local evaluation reports for the same cause.
    /// </summary>
    /// <param name="flag">The flag definition.</param>
    /// <returns>
    /// <see cref="UnresolvedFlagReason.ExperienceContinuity"/>, <see cref="UnresolvedFlagReason.UnsupportedDefinition"/>,
    /// or <c>null</c> when the definition alone does not stop local evaluation.
    /// </returns>
    public UnresolvedFlagReason? GetDefinitionUnresolvedReason(LocalFeatureFlag flag)
    {
        if (!flag.Active)
        {
            return null;
        }

        if (flag.EnsureExperienceContinuity)
        {
            return UnresolvedFlagReason.ExperienceContinuity;
        }

        if (flag.Filters?.AggregationGroupTypeIndex is { } aggregationGroupIndex
            && !_groupTypeMapping.ContainsKey(aggregationGroupIndex))
        {
            return UnresolvedFlagReason.UnsupportedDefinition;
        }

        var visitedCohortIds = new HashSet<long>();
        var usesUnsupportedFilter = (flag.Filters?.Groups ?? [])
            .SelectMany(condition => condition.Properties ?? [])
            .Any(filter => IsUnsupportedFilter(filter, visitedCohortIds, inCohort: false));
        return usesUnsupportedFilter ? UnresolvedFlagReason.UnsupportedDefinition : null;
    }

    // Returns null when the definition cannot be read. The load-time warning already logs that, and a failed lookup
    // must not stop the server fallback.
    UnresolvedFlagReason? TryGetDefinitionUnresolvedReason(LocalFeatureFlag flag)
    {
        try
        {
            return GetDefinitionUnresolvedReason(flag);
        }
#pragma warning disable CA1031 // The reason is diagnostic only. A malformed definition must not fail a flag that the server can evaluate.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    // Returns the server exception with a higher-precedence reason found elsewhere in the flag, so the flag still
    // falls back to the server.
    static RequiresServerEvaluationException WithReason(RequiresServerEvaluationException e, UnresolvedFlagReason reason)
        => reason == e.Reason ? e : new RequiresServerEvaluationException(e.Message, reason, e);

    bool IsUnsupportedFilter(Filter filter, HashSet<long> visitedCohortIds, bool inCohort)
    {
        switch (filter)
        {
            case FilterSet filterSet:
                return (filterSet.Values ?? []).Any(child => IsUnsupportedFilter(child, visitedCohortIds, inCohort));
            case PropertyFilter property when inCohort && IsCohortRegexUnsupported(property):
                return true;
            case PropertyFilter { Type: FilterType.Flag } dependency:
                // MatchProperty cannot evaluate a flag dependency inside a cohort.
                if (inCohort)
                {
                    return true;
                }
                try
                {
                    ValidateFlagDependencyFilter(dependency);
                }
                catch (InconclusiveMatchException)
                {
                    return true;
                }
                return dependency.DependencyChain!.Any(key => !_localFeatureFlags.ContainsKey(key));
            case PropertyFilter { Type: FilterType.Cohort } cohort:
                if (cohort.Value?.CohortId is not { } cohortId || !_cohortFilters.TryGetValue(cohortId, out var cohortFilters))
                {
                    return true;
                }
                return visitedCohortIds.Add(cohortId) && IsUnsupportedFilter(cohortFilters, visitedCohortIds, inCohort: true);
            case PropertyFilter property:
                try
                {
                    AssertFilterSupported(property);
                    return false;
                }
                catch (InconclusiveMatchException)
                {
                    return true;
                }
            default:
                return false;
        }
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

        if (!flag.Active)
        {
            var result = false;
            evaluationCache[flag.Key] = result;
            return result;
        }

        if (flag.EnsureExperienceContinuity)
        {
            throw new InconclusiveMatchException(
                $"Flag \"{flag.Key}\" has experience continuity enabled",
                UnresolvedFlagReason.ExperienceContinuity);
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
                throw new InconclusiveMatchException(
                    $"Flag has unknown group type index: {aggregationGroupIndex}",
                    UnresolvedFlagReason.UnsupportedDefinition);
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
        var flagAggregation = filters?.AggregationGroupTypeIndex;
        var earlyExit = filters?.EarlyExit ?? false;
        UnresolvedFlagReason? inconclusiveReason = null;
        var flagVariants = filters?.Multivariate?.Variants ?? [];

        foreach (var condition in flagConditions)
        {
            // Per-condition aggregation overrides only when the condition explicitly
            // sets its own AggregationGroupTypeIndex (mixed targeting). When absent,
            // fall back to the flag-level aggregation so existing pure person and
            // pure group flags keep their original behavior.
            var conditionAggregation = condition.AggregationGroupTypeIndex ?? flagAggregation;

            var effectiveProperties = properties;
            var effectiveBucketingId = distinctId;

            // The condition explicitly sets its own aggregation, different from the
            // flag level. Re-resolve properties and bucketing id from the condition's
            // group so this condition evaluates against that group.
            // conditionAggregation is non-null here: if it were null and flagAggregation
            // were also null they'd be equal, and the only way conditionAggregation can
            // be null at all (after the ?? above) is that case.
            if (conditionAggregation != flagAggregation)
            {
                if (!_groupTypeMapping.TryGetValue(conditionAggregation!.Value, out var groupType)
                    || groups is null
                    || !groups.TryGetGroup(groupType, out var group))
                {
                    // Skip this condition: group type unknown or not passed in.
                    continue;
                }
                effectiveProperties = group.Properties;
                effectiveBucketingId = group.GroupKey;
            }

            ConditionMatchResult? conditionResult = null;
            try
            {
                conditionResult = EvaluateConditionMatch(flag, effectiveBucketingId, condition, effectiveProperties, evaluationCache, groups);
            }
            catch (RequiresServerEvaluationException e)
            {
                // Static cohort or other missing server-side data - must fallback to API. Evaluation stops here, so
                // the definition of the whole flag also counts, including filters and cohorts it did not reach.
                var reason = UnresolvedFlagReasonExtensions.Pick(inconclusiveReason, e.Reason);
                if (TryGetDefinitionUnresolvedReason(flag) is { } definitionReason)
                {
                    reason = UnresolvedFlagReasonExtensions.Pick(reason, definitionReason);
                }
                throw WithReason(e, reason);
            }
            catch (InconclusiveMatchException e)
            {
                // Evaluation error (bad regex, invalid date, missing property, etc.)
                // Track that we had an inconclusive match, but try other conditions
                inconclusiveReason = UnresolvedFlagReasonExtensions.Pick(inconclusiveReason, e.Reason);
            }

            if (conditionResult is null)
            {
                // EvaluateConditionMatch threw InconclusiveMatchException above.
                continue;
            }

            // When early_exit is enabled and the condition's property filters matched (or
            // there were none) but the rollout excluded the user, stop evaluating later
            // condition groups and return a definitive disabled result. Mirrors the
            // server-side Rust evaluation engine. A pure property-filter mismatch always
            // falls through to the next condition group, even when early_exit is enabled.
            // This check is intentionally outside the try-catch so the throw propagates
            // to the caller and isn't re-caught as a regular inconclusive match.
            if (earlyExit && conditionResult == ConditionMatchResult.OutOfRolloutBound)
            {
                if (inconclusiveReason is { } reason)
                {
                    throw new InconclusiveMatchException("Can't determine if feature flag is enabled or not with given properties", reason);
                }
                return false;
            }

            if (conditionResult != ConditionMatchResult.Match)
            {
                continue;
            }

            var variantOverride = condition.Variant;
            var variant = variantOverride is not null
                          && flagVariants.Select(v => v.Key).Contains(variantOverride)
                ? variantOverride
                : GetMatchingVariant(flag, effectiveBucketingId);

            return variant is not null
                ? new StringOrValue<bool>(variant)
                : true;
        }

        if (inconclusiveReason is { } unresolvedReason)
        {
            throw new InconclusiveMatchException("Can't determine if feature flag is enabled or not with given properties", unresolvedReason);
        }

        // We can only return False when either all conditions are False, or
        // no condition was inconclusive.
        return false;
    }

    /// <summary>
    /// The tri-state outcome of evaluating a single condition group, mirroring the server-side
    /// Rust evaluation engine. Used to distinguish a property-filter mismatch from a
    /// rollout-percentage exclusion so that the <c>early_exit</c> behavior can short-circuit
    /// only on the latter.
    /// </summary>
    enum ConditionMatchResult
    {
        /// <summary>
        /// Property filters matched (or there were none) AND the rollout included the user.
        /// </summary>
        Match,

        /// <summary>
        /// A property filter did not match. Always falls through to the next condition group,
        /// even when <c>early_exit</c> is enabled.
        /// </summary>
        NoMatch,

        /// <summary>
        /// Property filters matched (or there were none) but the rollout excluded the user.
        /// When <c>early_exit</c> is enabled, this produces a definitive disabled result.
        /// </summary>
        OutOfRolloutBound
    }

    ConditionMatchResult EvaluateConditionMatch(
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
            // Filters in a condition have no defined order, so an inconclusive filter does not stop the loop:
            // a later filter can still rule the condition out or carry a higher-precedence reason.
            UnresolvedFlagReason? inconclusiveReason = null;
            RequiresServerEvaluationException? serverEvaluationException = null;
            foreach (var property in condition.Properties)
            {
                bool isMatch;
                try
                {
                    if (property.Type is not (FilterType.Cohort or FilterType.Flag) && IsPropertyMissing(property, properties))
                    {
                        // Same result as MatchProperty for an absent key, without the cost of throwing for every
                        // missing filter.
                        AssertFilterSupported(property);
                        inconclusiveReason = UnresolvedFlagReasonExtensions.Pick(inconclusiveReason, UnresolvedFlagReason.MissingContext);
                        continue;
                    }

                    isMatch = property.Type switch
                    {
                        FilterType.Cohort => MatchCohort(property, distinctId, properties),
                        FilterType.Flag => MatchFlagDependencyFilter(property, distinctId, properties, evaluationCache, groups),
                        _ => MatchProperty(property, distinctId, properties) ?? false
                    };
                }
                catch (RequiresServerEvaluationException e)
                {
                    // Fall back to the server only when this is the first filter that could not be checked, as when
                    // the loop stopped there. After another inconclusive filter, later conditions must still be able
                    // to resolve the flag locally.
                    if (inconclusiveReason is null)
                    {
                        serverEvaluationException ??= e;
                    }
                    inconclusiveReason = UnresolvedFlagReasonExtensions.Pick(inconclusiveReason, e.Reason);
                    continue;
                }
                catch (InconclusiveMatchException e)
                {
                    inconclusiveReason = UnresolvedFlagReasonExtensions.Pick(inconclusiveReason, e.Reason);
                    continue;
                }
#pragma warning disable CA1031 // An earlier filter already left the condition inconclusive. Stop at that reason, so an unexpected error here cannot fail a flag that a later condition can still resolve.
                catch (Exception) when (inconclusiveReason is not null)
#pragma warning restore CA1031
                {
                    break;
                }

                if (!isMatch)
                {
                    return ConditionMatchResult.NoMatch;
                }
            }

            if (inconclusiveReason is { } reason)
            {
                // Throw the server exception again, so MatchFeatureFlagProperties stops at it and falls back to the
                // server instead of trying later conditions.
                if (serverEvaluationException is not null)
                {
                    throw WithReason(serverEvaluationException, reason);
                }
                throw new InconclusiveMatchException($"Can't determine if a condition of flag {flag.Key} matches", reason);
            }
        }

        // Property filters matched (or there were none). Now check the rollout percentage.
        if (rolloutPercentage is 100)
        {
            return ConditionMatchResult.Match;
        }

        var hashValue = Hash(flag.Key, distinctId);
        return hashValue > rolloutPercentage / 100.0
            ? ConditionMatchResult.OutOfRolloutBound
            : ConditionMatchResult.Match;
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
        var cohortId = filter.Value?.CohortId;
        if (cohortId is null || !_cohortFilters.TryGetValue(cohortId.Value, out var conditions))
        {
            throw new RequiresServerEvaluationException(
                $"cohort {cohortId} not found in local cohorts - likely a static cohort that requires server evaluation");
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

        UnresolvedFlagReason? inconclusiveReason = null;

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
                    inconclusiveReason = UnresolvedFlagReasonExtensions.Pick(inconclusiveReason, e.Reason);
                }
            }

            if (inconclusiveReason is { } reason)
            {
                throw new InconclusiveMatchException("Can't match cohort without a given cohort property value", reason);
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
                if (filter.Type is not (FilterType.Cohort or FilterType.Flag) && IsPropertyMissing(propertyFilter, propertyValues))
                {
                    // Same result as MatchProperty for an absent key, without the cost of throwing for every
                    // missing filter.
                    AssertFilterSupported(propertyFilter);
                    if (IsCohortRegexUnsupported(propertyFilter))
                    {
                        throw new InconclusiveMatchException(
                            "Invalid regex cohort property requires server evaluation",
                            UnresolvedFlagReason.UnsupportedDefinition);
                    }
                    inconclusiveReason = UnresolvedFlagReasonExtensions.Pick(inconclusiveReason, UnresolvedFlagReason.MissingContext);
                    continue;
                }

                var isMatch = filter.Type is FilterType.Cohort
                    ? MatchCohort(propertyFilter, distinctId, propertyValues)
                    : MatchProperty(propertyFilter, distinctId, propertyValues)
                        ?? throw new InconclusiveMatchException(
                            "Invalid regex cohort property requires server evaluation",
                            UnresolvedFlagReason.UnsupportedDefinition);
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
                inconclusiveReason = UnresolvedFlagReasonExtensions.Pick(inconclusiveReason, e.Reason);
            }
        }

        if (inconclusiveReason is { } unresolvedReason)
        {
            throw new InconclusiveMatchException("Can't match cohort without a given cohort property value", unresolvedReason);
        }

        // if we get here, all matched in AND case, or none matched in OR case
        return filterSet.Type is FilterType.And;
    }

    /// <summary>
    /// Evaluates a feature flag for a given set of properties.
    /// </summary>
    /// <remarks>
    /// Only looks for matches where the key exists in properties.
    /// </remarks>
    /// <param name="propertyFilter">The <see cref="PropertyFilter"/> to evaluate.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="properties">The overriden values that describe the user/group.</param>
    /// <returns>A match result, or <c>null</c> for an invalid regex, which must not be negated by a cohort.</returns>
    bool? MatchProperty(PropertyFilter propertyFilter, string distinctId, Dictionary<string, object?>? properties)
    {
        // Handle flag dependencies
        if (propertyFilter.Type is FilterType.Flag)
        {
            // Flag dependencies can't be evaluated from this context, throw inconclusive
            throw new InconclusiveMatchException(
                $"Flag dependency '{propertyFilter.Key}' cannot be evaluated without evaluation context",
                UnresolvedFlagReason.UnsupportedDefinition);
        }

        var key = NotNull(propertyFilter.Key);

        // The overrideValue is the value that the user or group has set for the property. It's called "override value"
        // because when passing it to the `/flags` endpoint, it overrides the values stored in PostHog. For local
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
            // An unusable filter outranks missing context, so check the filter before blaming the properties.
            AssertFilterSupported(propertyFilter);
            throw new InconclusiveMatchException(
                "Can't match properties without a given property value",
                UnresolvedFlagReason.MissingContext);
        }

        if (propertyFilter.Operator is ComparisonOperator.IsSet or ComparisonOperator.IsNotSet)
        {
            return propertyFilter.Operator is ComparisonOperator.IsSet;
        }

        if (propertyFilter.Value is not { } value)
        {
            throw new InconclusiveMatchException("The filter property value is null", UnresolvedFlagReason.UnsupportedDefinition);
        }

        if (overrideValue is null
            && propertyFilter.Operator is not (
                ComparisonOperator.Exact
                or ComparisonOperator.IsNot
                or ComparisonOperator.ContainsIgnoreCase
                or ComparisonOperator.DoesNotContainIgnoreCase
                or ComparisonOperator.StartsWith
                or ComparisonOperator.NotStartsWith
                or ComparisonOperator.EndsWith
                or ComparisonOperator.NotEndsWith
                or ComparisonOperator.Regex
                or ComparisonOperator.NotRegex))
        {
            // The backend stringifies null for string operators and exact matching. Other value operators keep the
            // existing null non-match behavior.
            return false;
        }

        // A new operator here also needs an arm in AssertFilterSupported.
        return propertyFilter.Operator switch
        {
            ComparisonOperator.Exact => value.IsExactMatch(overrideValue, LocalEvaluationApiResult.PropertyMatchingVersion),
            ComparisonOperator.IsNot => !value.IsExactMatch(overrideValue, LocalEvaluationApiResult.PropertyMatchingVersion),
            ComparisonOperator.GreaterThan => value < overrideValue,
            ComparisonOperator.GreaterThanOrEquals => value <= overrideValue,
            ComparisonOperator.LessThan => value > overrideValue,
            ComparisonOperator.LessThanOrEquals => value >= overrideValue,
            ComparisonOperator.ContainsIgnoreCase => value.IsContainedByAsciiIgnoreCase(overrideValue),
            ComparisonOperator.DoesNotContainIgnoreCase => !value.IsContainedByAsciiIgnoreCase(overrideValue),
            ComparisonOperator.StartsWith => value.IsPrefixOfAsciiIgnoreCase(overrideValue),
            ComparisonOperator.NotStartsWith => !value.IsPrefixOfAsciiIgnoreCase(overrideValue),
            ComparisonOperator.EndsWith => value.IsSuffixOfAsciiIgnoreCase(overrideValue),
            ComparisonOperator.NotEndsWith => !value.IsSuffixOfAsciiIgnoreCase(overrideValue),
            ComparisonOperator.Regex => value.MatchFeatureFlagRegex(overrideValue),
            ComparisonOperator.NotRegex => !value.MatchFeatureFlagRegex(overrideValue),
            ComparisonOperator.IsDateBefore => value.IsDateBefore(overrideValue, _timeProvider.GetUtcNow()),
            ComparisonOperator.IsDateAfter => !value.IsDateBefore(overrideValue, _timeProvider.GetUtcNow()),
            ComparisonOperator.SemverEquals => value.CompareSemver(overrideValue) == 0,
            ComparisonOperator.SemverNotEquals => value.CompareSemver(overrideValue) != 0,
            ComparisonOperator.SemverGreaterThan => value.CompareSemver(overrideValue) > 0,
            ComparisonOperator.SemverGreaterThanOrEquals => value.CompareSemver(overrideValue) >= 0,
            ComparisonOperator.SemverLessThan => value.CompareSemver(overrideValue) < 0,
            ComparisonOperator.SemverLessThanOrEquals => value.CompareSemver(overrideValue) <= 0,
            ComparisonOperator.SemverTilde => value.IsSemverTildeMatch(overrideValue),
            ComparisonOperator.SemverCaret => value.IsSemverCaretMatch(overrideValue),
            ComparisonOperator.SemverWildcard => value.IsSemverWildcardMatch(overrideValue),
            null => true, // If no operator is specified, just return true.
            _ => throw new InconclusiveMatchException($"Unknown operator: {propertyFilter.Operator}", UnresolvedFlagReason.UnsupportedDefinition)
        };
    }

    // A cohort reports a regex it cannot run as UnsupportedDefinition. At flag level the same regex
    // resolves locally as no match, so this check stays out of AssertFilterSupported.
    static bool IsCohortRegexUnsupported(PropertyFilter propertyFilter)
        => propertyFilter is { Operator: ComparisonOperator.Regex or ComparisonOperator.NotRegex, Value: { } value }
           && (value.StringValue is not { } pattern || !RegexHelpers.CanMatchLocally(pattern));

    static bool IsPropertyMissing(PropertyFilter propertyFilter, Dictionary<string, object?>? properties)
        => properties is not null
           && !string.Equals(propertyFilter.Key, PostHogProperties.DistinctId, StringComparison.Ordinal)
           && !properties.ContainsKey(propertyFilter.Key);

    /// <summary>
    /// Throws an <see cref="InconclusiveMatchException"/> with the <see cref="UnresolvedFlagReason.UnsupportedDefinition"/>
    /// reason when local evaluation cannot use the filter, whatever the properties hold: an unknown operator, or a
    /// filter value that the operator cannot parse.
    /// </summary>
    // Keep the operator list in sync with the switch in MatchProperty. An operator missing here is reported as
    // unsupported when the property is absent, and in the load-time warning.
    static void AssertFilterSupported(PropertyFilter propertyFilter)
    {
        if (propertyFilter.Operator is ComparisonOperator.IsSet or ComparisonOperator.IsNotSet)
        {
            return;
        }

        if (propertyFilter.Value is not { } value)
        {
            throw new InconclusiveMatchException("The filter property value is null", UnresolvedFlagReason.UnsupportedDefinition);
        }

        switch (propertyFilter.Operator)
        {
            case null
                or ComparisonOperator.Exact
                or ComparisonOperator.IsNot
                or ComparisonOperator.GreaterThan
                or ComparisonOperator.GreaterThanOrEquals
                or ComparisonOperator.LessThan
                or ComparisonOperator.LessThanOrEquals
                or ComparisonOperator.ContainsIgnoreCase
                or ComparisonOperator.DoesNotContainIgnoreCase
                or ComparisonOperator.StartsWith
                or ComparisonOperator.NotStartsWith
                or ComparisonOperator.EndsWith
                or ComparisonOperator.NotEndsWith:
                return;
            case ComparisonOperator.Regex or ComparisonOperator.NotRegex:
                if (value.StringValue is { } pattern && !RegexHelpers.IsCompatiblePattern(pattern))
                {
                    throw new InconclusiveMatchException("Regex syntax requires server evaluation", UnresolvedFlagReason.UnsupportedDefinition);
                }
                return;
            case ComparisonOperator.IsDateBefore or ComparisonOperator.IsDateAfter:
                value.ParseFilterDate();
                return;
            case ComparisonOperator.SemverEquals
                or ComparisonOperator.SemverNotEquals
                or ComparisonOperator.SemverGreaterThan
                or ComparisonOperator.SemverGreaterThanOrEquals
                or ComparisonOperator.SemverLessThan
                or ComparisonOperator.SemverLessThanOrEquals
                or ComparisonOperator.SemverTilde
                or ComparisonOperator.SemverCaret:
                value.ParseFilterSemver();
                return;
            case ComparisonOperator.SemverWildcard:
                value.ParseFilterWildcard();
                return;
            default:
                throw new InconclusiveMatchException($"Unknown operator: {propertyFilter.Operator}", UnresolvedFlagReason.UnsupportedDefinition);
        }
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
            : throw new InconclusiveMatchException(
                $"Flag dependency '{flagKey}' is missing in evaluation cache",
                UnresolvedFlagReason.UnsupportedDefinition);

        // Match the value against expectations
        return MatchesDependencyValue(propertyValue, immediateDependencyValue);
    }

    static (string flagKey, PropertyFilterValue propertyValue) ValidateFlagDependencyFilter(PropertyFilter propertyFilter)
    {
        var flagKey = NotNull(propertyFilter.Key);

        if (propertyFilter.Value is not { } propertyValue)
        {
            throw new InconclusiveMatchException("The filter property value is null", UnresolvedFlagReason.UnsupportedDefinition);
        }

        // Check if dependency_chain is present - it should always be provided for flag dependencies
        if (propertyFilter.DependencyChain is null)
        {
            throw new InconclusiveMatchException(
                $"Flag dependency property for '{flagKey}' is missing required 'dependency_chain' field",
                UnresolvedFlagReason.UnsupportedDefinition);
        }

        // Handle circular dependency (empty chain means circular)
        if (propertyFilter.DependencyChain.Count == 0)
        {
            // Circular dependencies should evaluate to inconclusive as per PostHog API design
            throw new InconclusiveMatchException(
                $"Flag dependency property for '{flagKey}' has an empty 'dependency_chain' field indicating a circular dependency",
                UnresolvedFlagReason.UnsupportedDefinition);
        }

        // The last item in the dependency chain should be the value in the current property filter.
        if (propertyFilter.DependencyChain[^1] != flagKey)
        {
            throw new InconclusiveMatchException(
                $"Flag dependency property for '{flagKey}' has an invalid 'dependency_chain' field - last item should be the flag key itself",
                UnresolvedFlagReason.UnsupportedDefinition);
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
        // A dependency with no definition outranks one that is only inconclusive, so check the whole chain before
        // evaluating any of it.
        var missingDependency = dependencyChain.FirstOrDefault(
            depFlagKey => !evaluationCache.ContainsKey(depFlagKey) && !_localFeatureFlags.ContainsKey(depFlagKey));
        if (missingDependency is not null)
        {
            throw new InconclusiveMatchException(
                $"Cannot evaluate flag dependency '{missingDependency}' - flag not found in local flags",
                UnresolvedFlagReason.UnsupportedDefinition);
        }

        // Evaluate all dependencies in the chain order and cache the results.
        foreach (var depFlagKey in dependencyChain)
        {
            if (evaluationCache.ContainsKey(depFlagKey))
            {
                continue;
            }

            evaluationCache[depFlagKey] = EvaluateSingleDependency(_localFeatureFlags[depFlagKey], distinctId, properties, evaluationCache, groups);
        }
    }

    // Evaluates a single flag dependency and returns the result.
    StringOrValue<bool> EvaluateSingleDependency(
        LocalFeatureFlag depFlag,
        string distinctId,
        Dictionary<string, object?>? properties,
        Dictionary<string, StringOrValue<bool>> evaluationCache,
        GroupCollection? groups)
    {
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
            // If we can't evaluate a dependency, propagate the error. The dependency's own reason belongs to the
            // dependency, not to the flag that depends on it.
            throw new InconclusiveMatchException(
                $"Cannot evaluate flag dependency '{depFlag.Key}': {e.Message}",
                UnresolvedFlagReason.UnresolvedDependency,
                e);
        }
        catch (RequiresServerEvaluationException e)
        {
            // Keep the server exception, so the dependent flag also falls back to the server, which has the data.
            throw new RequiresServerEvaluationException(
                $"Cannot evaluate flag dependency '{depFlag.Key}': {e.Message}",
                UnresolvedFlagReason.UnresolvedDependency,
                e);
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