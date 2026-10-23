using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using PostHog.Json;

namespace PostHog.Library;

internal static class RegexHelpers
{
    /// <summary>
    /// Validates that the pattern is a valid regular expression and if so, returns a <see cref="Regex"/> object.
    /// </summary>
    /// <param name="pattern">The regular expression pattern.</param>
    /// <param name="regex">The <see cref="Regex"/> object if the pattern is valid.</param>
    /// <param name="options">The <see cref="RegexOptions"/> to pass to the created <see cref="Regex"/>.</param>
    /// <returns><c>true</c> if the pattern is a valid regular expression. Otherwise <c>false</c></returns>
    public static bool TryValidateRegex(
        string pattern,
        [NotNullWhen(true)] out Regex? regex,
        RegexOptions options)
    {
        regex = null;
        try
        {
            regex = new Regex(pattern, options);
            return true;
        }
#if NETSTANDARD2_0 || NETSTANDARD2_1
        catch (ArgumentException)
#else
        catch (RegexParseException)
#endif
        {
            return false;
        }
    }

    // null denotes invalid shared syntax, not a failed search that not_regex may negate.
    public static bool? MatchFeatureFlagRegex(string pattern, string input)
    {
        if (pattern.Length == 0)
        {
            return true;
        }

        var compatiblePattern = GetCompatiblePattern(pattern)
            ?? throw new InconclusiveMatchException("Regex syntax requires server evaluation");
        Regex regex;
        try
        {
            regex = new Regex(compatiblePattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return null;
        }

        // .NET matches UTF-16 code units and its Unicode classes differ from the service's.
        if (input.Any(character => character > 0x7f))
        {
            throw new InconclusiveMatchException("Unicode regex matching requires server evaluation");
        }

        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new InconclusiveMatchException("Regex execution requires server evaluation");
        }
    }

    // Successful .NET compilation alone does not establish compatibility with the service's regex syntax.
    static string? GetCompatiblePattern(string pattern)
    {
        var result = new StringBuilder();
        var classStart = -1;
        var previousToken = '\0';
        var previousEmptyGroup = false;
        var previousShorthand = false;
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character > 0x7f)
            {
                return null;
            }

            if (character == '\\')
            {
                if (++index == pattern.Length)
                {
                    result.Append('\\');
                    break;
                }

                var escaped = pattern[index];
                if (escaped == 'x')
                {
                    if (index + 2 >= pattern.Length || !IsHex(pattern[index + 1]) || !IsHex(pattern[index + 2]))
                    {
                        return null;
                    }
                    // .NET's variable-width \\x can consume two more hex digits than Rust's \\xNN.
                    result.Append("\\u00").Append(pattern[index + 1]).Append(pattern[index + 2]);
                    index += 2;
                }
                else
                {
                    var shorthand = escaped is 'd' or 'D' or 's' or 'S' or 'w' or 'W';
                    var control = escaped is 'f' or 'n' or 'r' or 't' or 'v' or 'b';
                    var punctuation = escaped is >= ' ' and <= '~' && !char.IsLetterOrDigit(escaped);
                    if ((!shorthand && !control && !punctuation)
                        || (classStart < 0 && escaped is 'b' or 'B' or '<' or '>')
                        || (classStart >= 0 && punctuation && !"\\.*+?()|[]{}^$-".Contains(escaped.ToString(), StringComparison.Ordinal)))
                    {
                        // Outside-class word boundaries can exhaust the service's 10,000-step budget.
                        return null;
                    }
                    if (punctuation && classStart < 0)
                    {
                        result.Append(Regex.Escape(escaped.ToString()));
                    }
                    else
                    {
                        result.Append('\\').Append(escaped);
                    }
                }
                previousShorthand = escaped is 'd' or 'D' or 's' or 'S' or 'w' or 'W';
                previousToken = 'a';
                previousEmptyGroup = false;
                continue;
            }

            if (character == '[')
            {
                if (classStart >= 0)
                {
                    return null;
                }
                classStart = index;
                previousShorthand = false;
            }
            else if (classStart >= 0)
            {
                var contentStart = classStart + (pattern[classStart + 1] == '^' ? 2 : 1);
                if (character == ']')
                {
                    if (index == contentStart)
                    {
                        return null;
                    }
                    classStart = -1;
                }
                else
                {
                    if (index + 1 < pattern.Length
                        && ((character is '&' or '-' or '~' && pattern[index + 1] == character)
                            || (character == '-' && index > contentStart && pattern[index + 1] != ']'
                                && (previousShorthand || pattern[index + 1] == '\\'))))
                    {
                        return null;
                    }
                    previousShorthand = false;
                    result.Append(character);
                    continue;
                }
            }
            else if (character == '(' && index + 1 < pattern.Length && pattern[index + 1] == '?')
            {
                if (index + 2 >= pattern.Length || pattern[index + 2] != ':')
                {
                    return null;
                }
                result.Append("(?:");
                index += 2;
                previousToken = '(';
                previousEmptyGroup = false;
                continue;
            }
            else if (character == '{')
            {
                if (previousToken is '\0' or '(' or '|' or '^' or '$' or '*' or '+' or '?' or '}' || previousEmptyGroup)
                {
                    return null;
                }
                var end = GetRepetitionEnd(pattern, index);
                if (end < 0 || (end + 1 < pattern.Length && pattern[end + 1] is '*' or '+' or '{'))
                {
                    return null;
                }
                result.Append(pattern, index, end - index + 1);
                index = end;
                previousToken = '}';
                continue;
            }
            else if (character is '}' or ']')
            {
                return null;
            }
            else if (character is '*' or '+' or '?')
            {
                if (previousToken is '^' or '$' || previousEmptyGroup
                    || (index + 1 < pattern.Length && pattern[index + 1] is '+' or '{')
                    || (index + 1 < pattern.Length && pattern[index + 1] == '*'
                        && previousToken is not ('\0' or '(' or '|'))
                    || (character == '?' && previousToken is '*' or '+' or '?' or '}'
                        && index + 1 < pattern.Length && pattern[index + 1] == '?'))
                {
                    return null;
                }
            }

            previousEmptyGroup = character == ')' && (previousToken == '(' || previousEmptyGroup);
            previousToken = character;
            // Rust's default $ is strict; .NET's also matches before the final newline.
            if (character == '$' && classStart < 0)
            {
                result.Append("\\z");
            }
            else
            {
                result.Append(character);
            }
        }
        return result.ToString();
    }

    static bool IsHex(char character) => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    static int GetRepetitionEnd(string pattern, int start)
    {
        var index = start + 1;
        if (!ReadCount(pattern, ref index))
        {
            return -1;
        }
        if (index < pattern.Length && pattern[index] == ',')
        {
            index++;
            if (index < pattern.Length && pattern[index] != '}' && !ReadCount(pattern, ref index))
            {
                return -1;
            }
        }
        return index < pattern.Length && pattern[index] == '}' ? index : -1;
    }

    static bool ReadCount(string pattern, ref int index)
    {
        var start = index;
        long count = 0;
        while (index < pattern.Length && pattern[index] is >= '0' and <= '9')
        {
            count = count * 10 + pattern[index++] - '0';
            if (count > int.MaxValue)
            {
                return false;
            }
        }
        return index > start;
    }
}
