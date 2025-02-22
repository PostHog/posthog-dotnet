using System.Net;
using PostHog.Api;

namespace PostHog.Library;

/// <summary>
/// Exception thrown when we run into an error while interacting with the PostHog API.
/// </summary>
public class ApiException : Exception
{
    public ApiException()
    {
    }

    public ApiException(string message) : base(message)
    {
    }

    public ApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ApiException(ApiErrorResult? error, HttpStatusCode statusCode, Exception? innerException)
        : base(error?.Detail, innerException)
    {
        Code = error?.Code;
        ErrorType = error?.Type;
        Attr = error?.Attr;
        Status = statusCode;
    }

    public string? Code { get; }

    public string? ErrorType { get; }

    public string? Attr { get; }

    public HttpStatusCode Status { get; }
}