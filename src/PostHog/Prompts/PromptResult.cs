using System.Text.Json;
using static PostHog.Library.Ensure;

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

    /// <summary>
    /// Returns <see cref="Prompt"/> with each <c>{{name}}</c> placeholder replaced by the value of the variable
    /// with that name. A placeholder with no matching variable stays as it is.
    /// </summary>
    /// <param name="variables">
    /// The values by placeholder name. A name can contain letters, digits, <c>_</c>, <c>.</c> and <c>-</c>.
    /// </param>
    /// <returns>The prompt text with the placeholders filled in.</returns>
    /// <remarks>
    /// Numbers and dates are written with the invariant culture, so the text is the same on every machine.
    /// Booleans are written as <c>true</c> and <c>false</c>.
    /// </remarks>
    public string Compile(IReadOnlyDictionary<string, object> variables)
        => PromptTemplate.Compile(Prompt, NotNull(variables));

    /// <summary>
    /// Returns the event properties that link an LLM event to this prompt: <c>$ai_prompt_name</c> and
    /// <c>$ai_prompt_version</c>. Add them to the <c>$ai_generation</c> event you capture for the LLM call, and
    /// PostHog shows which prompt and version produced the output.
    /// </summary>
    /// <returns>
    /// A new dictionary with the two properties. You can add your other event properties to it. It is empty when
    /// <see cref="Source"/> is <see cref="PromptSource.CodeFallback"/>, because no PostHog prompt was used.
    /// </returns>
    public Dictionary<string, object> ToTraceProperties()
    {
        var properties = new Dictionary<string, object>();
        if (Name is not null)
        {
            properties["$ai_prompt_name"] = Name;
        }
        if (Version is not null)
        {
            properties["$ai_prompt_version"] = Version.Value;
        }
        return properties;
    }
}
