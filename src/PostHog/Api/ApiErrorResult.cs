namespace PostHog.Api;

/// <summary>
/// Body of an API error response.
/// </summary>
/// <param name="Type">The type of error.</param>
/// <param name="Code">The error code.</param>
/// <param name="Detail">Information about the error.</param>
/// <param name="Attr">Additional context about the error.</param>
public record ApiErrorResult(string Type, string Code, string Detail, string? Attr);
