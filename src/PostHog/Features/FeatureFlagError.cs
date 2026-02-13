namespace PostHog.Features;

/// <summary>
/// Error type constants for the $feature_flag_error property.
/// These values are sent in analytics events to track flag evaluation failures.
/// They should not be changed without considering impact on existing dashboards
/// and queries that filter on these values.
/// </summary>
internal static class FeatureFlagError
{
    /// <summary>
    /// Server returned errorsWhileComputingFlags=true
    /// </summary>
    public const string ErrorsWhileComputingFlags = "errors_while_computing_flags";

    /// <summary>
    /// Requested flag not in API response
    /// </summary>
    public const string FlagMissing = "flag_missing";

    /// <summary>
    /// Rate/quota limit exceeded (when quotaLimited contains "feature_flags")
    /// </summary>
    public const string QuotaLimited = "quota_limited";

    /// <summary>
    /// Request timed out
    /// </summary>
    public const string Timeout = "timeout";

    /// <summary>
    /// Network connectivity issue
    /// </summary>
    public const string ConnectionError = "connection_error";

    /// <summary>
    /// Unexpected exception during request
    /// </summary>
    public const string UnknownError = "unknown_error";

    /// <summary>
    /// Generate API error string with status code.
    /// </summary>
    /// <param name="statusCode">HTTP status code from the API error</param>
    /// <returns>Error string like "api_error_500"</returns>
    public static string ApiError(int statusCode) => $"api_error_{statusCode}";
}
