using System.Net;
using PostHog.Api;

namespace PostHog.Library;

/// <summary>
/// Exception thrown when we run into an error while interacting with the PostHog API.
/// </summary>
public class ApiException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class.
    /// </summary>
    public ApiException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ApiException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public ApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class from an API error response.
    /// </summary>
    /// <param name="error">The API error body, if one was returned.</param>
    /// <param name="statusCode">The HTTP status code returned by the API.</param>
    /// <param name="innerException">The exception that caused the API failure, if any.</param>
    public ApiException(ApiErrorResult? error, HttpStatusCode statusCode, Exception? innerException)
        : base(error?.Detail ?? $"API request failed with status code {(int)statusCode} ({statusCode}).", innerException)
    {
        Code = error?.Code;
        ErrorType = error?.Type;
        Attr = error?.Attr;
        Status = statusCode;
    }

    /// <summary>
    /// Gets the API-specific error code, if one was returned.
    /// </summary>
    public string? Code { get; }

    /// <summary>
    /// Gets the API-specific error type, if one was returned.
    /// </summary>
    public string? ErrorType { get; }

    /// <summary>
    /// Gets the API error attribute, if one was returned.
    /// </summary>
    public string? Attr { get; }

    /// <summary>
    /// Gets the HTTP status code returned by the API.
    /// </summary>
    public HttpStatusCode Status { get; }
}