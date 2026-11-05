using System.Text.Json;

namespace PostHog.Prompts;

/// <summary>
/// A prompt returned by <see cref="IPostHogPrompts.GetAsync"/>.
/// </summary>
public sealed class PromptResult
{
    /// <summary>
    /// Where the prompt came from.
    /// </summary>
    public required PromptSource Source { get; init; }

    /// <summary>
    /// The prompt text. It can contain <c>{{variable}}</c> placeholders.
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// The prompt name. <c>null</c> when <see cref="Source"/> is <see cref="PromptSource.CodeFallback"/>.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The version that was served. <c>null</c> when <see cref="Source"/> is <see cref="PromptSource.CodeFallback"/>.
    /// </summary>
    public int? Version { get; init; }

    /// <summary>
    /// The label the prompt was fetched by, when <see cref="GetPromptOptions.Label"/> was set. Otherwise <c>null</c>.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// The JSON object of model parameters or agent configuration stored with this version, such as
    /// <c>{"model": "gpt-4o", "temperature": 0.2}</c>. <c>null</c> when the version has none, and on a
    /// <see cref="PromptSource.CodeFallback"/> result.
    /// </summary>
    public JsonElement? Config { get; init; }
}
