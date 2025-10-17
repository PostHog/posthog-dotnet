using System.Text;
using System.Text.Json;
using PostHog.Json;
using PostHog.Library;

namespace PostHog.Features;

/// <summary>
/// Generates stable cache keys for feature flag results based on distinct ID, person properties, and groups.
/// </summary>
internal static class FeatureFlagCacheKey
{
    /// <summary>
    /// Generates a stable cache key from the provided parameters.
    /// The same inputs will always produce the same key, and different inputs will produce different keys.
    /// </summary>
    /// <param name="distinctId">The distinct ID of the user.</param>
    /// <param name="personProperties">Optional person properties that affect feature flag evaluation.</param>
    /// <param name="groups">Optional groups with their properties that affect feature flag evaluation.</param>
    /// <returns>A stable string cache key.</returns>
    public static string Generate(
        string distinctId,
        IReadOnlyDictionary<string, object?>? personProperties,
        GroupCollection? groups)
    {
        Ensure.NotNull(distinctId);

        var builder = new StringBuilder();
        builder.Append(distinctId);

        if (personProperties is { Count: > 0 })
        {
            builder.Append('|');
            builder.Append("p:");
            builder.Append(SerializeDictionary(personProperties));
        }

        if (groups is { Count: > 0 })
        {
            builder.Append('|');
            builder.Append("g:");

            // Sort groups by type for stability
            var sortedGroups = groups.OrderBy(g => g.GroupType).ToArray();

            for (var i = 0; i < sortedGroups.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                var group = sortedGroups[i];
                builder.Append(group.GroupType);
                builder.Append('=');
                builder.Append(group.GroupKey);

                if (group.Properties is { Count: > 0 })
                {
                    builder.Append('[');
                    builder.Append(SerializeDictionary(group.Properties));
                    builder.Append(']');
                }
            }
        }

        return builder.ToString();
    }

    static string SerializeDictionary(IReadOnlyDictionary<string, object?> dictionary)
    {
        // Sort keys for stable serialization
        var sortedPairs = dictionary
            .OrderBy(kvp => kvp.Key)
            .ToArray();

        // Use JSON serialization for complex values
        return JsonSerializer.Serialize(sortedPairs, JsonSerializerHelper.Options);
    }
}
