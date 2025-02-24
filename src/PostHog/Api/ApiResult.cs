using PostHog.Json;

namespace PostHog.Api;

/// <summary>
/// Result of a PostHog API call.
/// </summary>
/// <remarks>
/// For Capture, this returns {"status": 1} if the event was captured successfully.
/// For Batch, this returns {"status": "Ok"} if all events were captured successfully.
/// </remarks>
/// <param name="Status">The status of the call.</param>
public record ApiResult(StringOrValue<int> Status);