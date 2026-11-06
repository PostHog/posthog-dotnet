#if NETSTANDARD2_0 || NETSTANDARD2_1
using System.Diagnostics.CodeAnalysis;
#endif
using System.Globalization;
using System.Text.RegularExpressions;

namespace PostHog.Prompts;

/// <summary>
/// Fills the <c>{{variable}}</c> placeholders of a prompt.
/// </summary>
internal static partial class PromptTemplate
{
    // The same placeholder syntax as the PostHog Python and Node SDKs: letters, digits, "_", "." and "-".
    const string PlaceholderPattern = @"\{\{([\w.-]+)\}\}";

    [GeneratedRegex(PlaceholderPattern, RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    public static string Compile(string template, IReadOnlyDictionary<string, object> variables)
        // One pass over the template, so a value that itself contains a placeholder is not filled in turn.
        => Placeholder().Replace(
            template,
            match => variables.TryGetValue(match.Groups[1].Value, out var value) ? Format(value) : match.Value);

    static string Format(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        // Lowercase, as in JSON, instead of the "True" and "False" that bool.ToString() gives.
        bool flag => flag ? "true" : "false",
        // The invariant culture keeps 1.5 from becoming "1,5" on a machine with another locale.
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}

#if NETSTANDARD2_0 || NETSTANDARD2_1
internal static partial class PromptTemplate
{
    static readonly Regex PlaceholderRegex = new(PlaceholderPattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static partial Regex Placeholder() => PlaceholderRegex;
}
#endif
