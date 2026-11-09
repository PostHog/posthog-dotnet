using System.Net;

namespace PostHog.Emails;

/// <summary>
/// PostHog did not queue the email or answer with its status, or the client can't tell whether it did.
/// </summary>
/// <remarks>
/// Branch on <see cref="Code"/>, PostHog's stable error code, or on <see cref="Name"/>, a coarse category of the
/// failure. The message never contains the secret key or the request body.
/// </remarks>
public sealed class EmailException : Exception
{
    const string UnexpectedErrorCode = "unexpected_error";

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailException"/> class with the code <c>unexpected_error</c>.
    /// </summary>
    public EmailException()
        : this(UnexpectedErrorCode, "Sending the email failed unexpectedly.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailException"/> class with the code <c>unexpected_error</c>.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public EmailException(string message)
        : this(UnexpectedErrorCode, message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailException"/> class with the code <c>unexpected_error</c>.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public EmailException(string message, Exception innerException)
        : this(UnexpectedErrorCode, message, innerException: innerException)
    {
    }

    internal EmailException(
        string code,
        string message,
        HttpStatusCode? statusCode = null,
        string? why = null,
        string? fix = null,
        Uri? docsUrl = null,
        string? field = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Name = NameOf(statusCode, code);
        StatusCode = statusCode;
        Why = why;
        Fix = fix;
        DocsUrl = docsUrl;
        Field = field;
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// PostHog's error code, such as <c>recipient_suppressed</c> or <c>quota_limited</c>, or a client code:
    /// <c>missing_secret_key</c>, <c>missing_app_host</c>, <c>insecure_app_host</c>, <c>client_disabled</c>, <c>client_closed</c>,
    /// <c>network_error</c>, <c>api_error</c> (an answer without a code), <c>attachment_upload_failed</c> (the file
    /// storage refused an upload), <c>client_authorization_header</c> (the <see cref="System.Net.Http.HttpClient"/>
    /// would send a default <c>Authorization</c> header to the file storage) or <c>unexpected_error</c>. The client checks <c>too_many_attachments</c> and
    /// <c>message_too_large</c> itself before any request, as PostHog would.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// A coarse category of the failure: <c>validation_error</c>, <c>missing_api_key</c>, <c>invalid_permission</c>,
    /// <c>not_found</c>, <c>concurrent_idempotent_requests</c>, <c>invalid_idempotent_request</c>,
    /// <c>rate_limit_exceeded</c>, <c>monthly_quota_exceeded</c>, <c>service_unavailable</c> or
    /// <c>application_error</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The HTTP status, when PostHog answered, or the file storage's for <c>attachment_upload_failed</c>.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// Why PostHog refused the request, when it said.
    /// </summary>
    public string? Why { get; }

    /// <summary>
    /// How to fix the request, when PostHog said.
    /// </summary>
    public string? Fix { get; }

    /// <summary>
    /// The docs page about this error, when PostHog named one.
    /// </summary>
    public Uri? DocsUrl { get; }

    /// <summary>
    /// The request field the error is about, when PostHog named one.
    /// </summary>
    public string? Field { get; }

    /// <summary>
    /// How long PostHog asked the client to wait before it sends again.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    static string NameOf(HttpStatusCode? statusCode, string code)
    {
        if (code == "attachment_upload_failed")
        {
            // The status is the file storage's, so PostHog's categories don't apply: a 401 or 403 there is an
            // expired or altered upload URL, not a missing key or scope.
            return statusCode is (HttpStatusCode)429 || (int?)statusCode >= 500
                ? "service_unavailable"
                : "application_error";
        }

        if (code == "missing_secret_key" || statusCode == HttpStatusCode.Unauthorized)
        {
            return "missing_api_key";
        }

        if (statusCode is null)
        {
            if (code == "network_error")
            {
                return "service_unavailable";
            }

            return code.StartsWith("invalid_", StringComparison.Ordinal) || code is "too_many_attachments" or "message_too_large"
                ? "validation_error"
                : "application_error";
        }

        return (int)statusCode switch
        {
            400 => "validation_error",
            403 => "invalid_permission",
            404 => "not_found",
            409 => code == "template_outdated" ? "validation_error" : "concurrent_idempotent_requests",
            422 => code == "idempotency_key_reused" ? "invalid_idempotent_request" : "validation_error",
            429 => code == "quota_limited" ? "monthly_quota_exceeded" : "rate_limit_exceeded",
            503 => "service_unavailable",
            _ => "application_error",
        };
    }
}
