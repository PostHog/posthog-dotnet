namespace PostHog.Api;

/// <summary>
/// Body of an API error response.
/// </summary>
/// <property name="Type">The type of error.</property>
/// <property name="Code">The error code.</property>
/// <property name="Detail">Information about the error.</property>
/// <property name="Attr">Additional context about the error.</property>
public record ApiErrorResult(string Type, string Code, string Detail, string? Attr);
