namespace PostHog.Api;

/// <summary>
/// Body of an API error response.
/// </summary>
/// <param name="Type">The type of error.</param>
/// <param name="Code">The error code.</param>
/// <param name="Detail">Information about the error.</param>
/// <param name="Attr">???</param>
internal record UnauthorizedApiResult(string Type, string Code, string Detail, object Attr);
