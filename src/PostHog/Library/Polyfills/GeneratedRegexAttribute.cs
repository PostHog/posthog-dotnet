#if NETSTANDARD2_0 || NETSTANDARD2_1
using System.Text.RegularExpressions;

namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class GeneratedRegexAttribute : Attribute
{
    public GeneratedRegexAttribute(string pattern) { }

    public GeneratedRegexAttribute(string pattern, RegexOptions options) { }
}
#endif