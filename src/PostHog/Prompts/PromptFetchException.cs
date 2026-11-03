using System.Net;

namespace PostHog.Prompts;

/// <summary>
/// Thrown when <see cref="IPostHogPrompts.GetAsync"/> cannot return a prompt: the fetch failed, nothing was cached,
/// and no fallback was given. <see cref="Code"/> says what went wrong.
/// </summary>
public sealed class PromptFetchException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PromptFetchException"/> class.
    /// </summary>
    public PromptFetchException() : this("unknown", "The prompt could not be fetched.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptFetchException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PromptFetchException(string message) : this("unknown", message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptFetchException"/> class with a message and an inner
    /// exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public PromptFetchException(string message, Exception innerException) : this("unknown", message, innerException: innerException)
    {
    }

    internal PromptFetchException(
        string code,
        string message,
        HttpStatusCode? statusCode = null,
        TimeSpan? retryAfter = null,
        string? promptName = null,
        int? version = null,
        string? label = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
        PromptName = promptName;
        Version = version;
        Label = label;
    }

    /// <summary>
    /// A stable code for the failure: <c>missing_secret_key</c>, <c>client_disabled</c>, <c>not_found</c>,
    /// <c>unauthorized</c>, <c>forbidden</c>, <c>rate_limited</c>, <c>http_error</c>, <c>network_error</c> or
    /// <c>invalid_response</c>.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// The HTTP status PostHog answered with, when the failure was an HTTP response.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// How long PostHog asked to wait before fetching again, from a <c>429</c> response's <c>Retry-After</c> header.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// The name of the prompt that was requested.
    /// </summary>
    public string? PromptName { get; }

    /// <summary>
    /// The version that was requested, when one was.
    /// </summary>
    public int? Version { get; }

    /// <summary>
    /// The label that was requested, when one was.
    /// </summary>
    public string? Label { get; }
}
