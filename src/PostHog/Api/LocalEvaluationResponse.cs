namespace PostHog.Api;

/// <summary>
/// Response from the local evaluation API that includes ETag for conditional requests.
/// Use the static factory methods to construct instances.
/// </summary>
internal readonly record struct LocalEvaluationResponse
{
    /// <summary>
    /// The local evaluation result. Null when <see cref="IsNotModified"/> is true or on error.
    /// </summary>
    public LocalEvaluationApiResult? Result { get; }

    /// <summary>
    /// The ETag value from the response, if present.
    /// </summary>
    public string? ETag { get; }

    /// <summary>
    /// True if the server returned 304 Not Modified.
    /// </summary>
    public bool IsNotModified { get; }

    LocalEvaluationResponse(LocalEvaluationApiResult? result, string? etag, bool isNotModified)
    {
        Result = result;
        ETag = etag;
        IsNotModified = isNotModified;
    }

    /// <summary>
    /// Creates a response for when the server returns 304 Not Modified.
    /// </summary>
    /// <param name="etag">The ETag to preserve for the next request.</param>
    public static LocalEvaluationResponse NotModified(string? etag) =>
        new(result: null, etag, isNotModified: true);

    /// <summary>
    /// Creates a response for a successful fetch with new data.
    /// </summary>
    /// <param name="result">The evaluation result from the server.</param>
    /// <param name="etag">The ETag from the response.</param>
    public static LocalEvaluationResponse Success(LocalEvaluationApiResult? result, string? etag) =>
        new(result, etag, isNotModified: false);

    /// <summary>
    /// Creates a response indicating a failure (no data, no caching).
    /// </summary>
    public static LocalEvaluationResponse Failure() =>
        new(result: null, etag: null, isNotModified: false);
}
