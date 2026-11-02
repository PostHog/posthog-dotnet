using System.Text.Json;

namespace PostHog.Api;

internal sealed record UnsupportedFilter(JsonElement Definition) : Filter;
