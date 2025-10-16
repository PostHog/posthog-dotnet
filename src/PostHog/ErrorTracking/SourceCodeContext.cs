namespace PostHog.ErrorTracking;

internal record class SourceCodeContext(
        ICollection<string>? PreContext,
        string? ContextLine,
        ICollection<string>? PostContext);
