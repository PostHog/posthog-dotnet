using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PostHog.Api;
using PostHog.Library;
using static PostHog.Library.Ensure;
using static PostHog.Library.SemanticVersion;

namespace PostHog.Json;

/// <summary>
/// Represents a filter property value (<see cref="PropertyFilter"/>). This is the value that is used to compare against
/// the value of a property in a user or group, often called the "override value".
/// </summary>
/// <remarks>
/// The supported types are limited to the types we store in filter property values.
/// </remarks>
[JsonConverter(typeof(PropertyFilterValueJsonConverter))]
public class PropertyFilterValue
{
    readonly IReadOnlyList<string>? _numericListValues;
    readonly bool? _booleanListValue;

    /// <summary>
    /// If this value is a string, this property will be set.
    /// </summary>
    public string? StringValue { get; }

    /// <summary>
    /// If this value is an array of strings, this property will be set.
    /// </summary>
    public IReadOnlyList<string>? ListOfStrings { get; }

    /// <summary>
    /// If this value is a boolean, this property will be set.
    /// </summary>
    public bool? BooleanValue { get; }

    /// <summary>
    /// Creates a new instance of <see cref="PropertyFilterValue"/> from the specified <paramref name="jsonElement"/>.
    /// </summary>
    /// <remarks>
    /// When creating a feature flag condition on PostHog, even if you specify a value for a numeric type,
    /// the value gets sent as a string.
    /// </remarks>
    /// <param name="jsonElement">A JsonElement</param>
    /// <returns>A <see cref="PropertyFilterValue"/>.</returns>
    public static PropertyFilterValue? Create(JsonElement jsonElement) =>
        jsonElement.ValueKind switch
        {
            JsonValueKind.String => jsonElement.GetString() is { } stringValue ? new PropertyFilterValue(stringValue) : null,
            JsonValueKind.Array when TryParseStringArray(
                jsonElement,
                out var stringArrayValue,
                out var numericListValues,
                out var booleanListValue)
                => new PropertyFilterValue(stringArrayValue, numericListValues, booleanListValue),
            JsonValueKind.Number => new PropertyFilterValue(jsonElement.GetInt64()),
            JsonValueKind.True => new PropertyFilterValue(true),
            JsonValueKind.False => new PropertyFilterValue(false),
            JsonValueKind.Undefined => null,
            JsonValueKind.Null => null,
            _ => throw new ArgumentException($"JsonValueKind: {jsonElement.ValueKind} is not supported for filter property values.", nameof(jsonElement))
        };

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyFilterValue"/> class with a list of string values.
    /// </summary>
    /// <param name="listOfStrings">The list of string values to match against.</param>
    public PropertyFilterValue(IReadOnlyList<string> listOfStrings)
    {
        ListOfStrings = NotNull(listOfStrings);
        _booleanListValue = TryGetBooleanListValue(ListOfStrings);
    }

    PropertyFilterValue(
        IReadOnlyList<string> listOfStrings,
        IReadOnlyList<string>? numericListValues,
        bool? booleanListValue)
    {
        ListOfStrings = listOfStrings;
        _numericListValues = numericListValues;
        _booleanListValue = booleanListValue;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyFilterValue"/> class with a cohort identifier.
    /// </summary>
    /// <param name="cohortId">The cohort identifier.</param>
    public PropertyFilterValue(long cohortId)
    {
        CohortId = cohortId;
    }

    /// <summary>
    /// The cohort ID for this property filter.
    /// </summary>
    /// <remarks>As far as I can tell, this is the only place we have a numeric.</remarks>
    public long? CohortId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyFilterValue"/> class with a string value.
    /// </summary>
    /// <param name="stringValue">The string value.</param>
    public PropertyFilterValue(string stringValue)
    {
        StringValue = stringValue;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyFilterValue"/> class with a boolean value.
    /// </summary>
    /// <param name="booleanValue">The boolean value.</param>
    public PropertyFilterValue(bool booleanValue)
    {
        BooleanValue = booleanValue;
    }

    /// <summary>
    /// Does a regular expression match on this instance with the specified <paramref name="input"/> instance.
    /// </summary>
    /// <param name="input">The value to search with a regex. For non-strings, we'll call ToString and run the regex.</param>
    /// <returns><c>true</c>If the current value is a valid regex and it matches the other value.</returns>
    public bool IsRegexMatch(object? input)
    {
        if (StringValue is null || !RegexHelpers.TryValidateRegex(StringValue, out var regex, RegexOptions.None))
        {
            return false;
        }

        return regex.IsMatch(NotNull(ToInvariantString(input)));
    }

    /// <summary>
    /// Returns a value indicating whether this instance is contained by the specified <paramref name="other"/> instance.
    /// </summary>
    /// <param name="other">The other value to compare to this one.</param>
    /// <param name="stringComparison">The type of comparison if these are strings.</param>
    /// <returns><c>true</c> if this instance contains the other.</returns>
    public bool IsContainedBy(object? other, StringComparison stringComparison) =>
        ToInvariantString(other) is { } comparandString
        && StringValue is not null
        && comparandString.Contains(StringValue, stringComparison);

    /// <summary>
    /// Returns a value indicating whether this instance is a prefix of the specified <paramref name="other"/> instance.
    /// </summary>
    /// <param name="other">The other value to compare to this one.</param>
    /// <param name="stringComparison">The type of comparison if these are strings.</param>
    /// <returns><c>true</c> if the other value starts with this instance.</returns>
    public bool IsPrefixOf(object? other, StringComparison stringComparison) =>
        ToInvariantString(other) is { } comparandString
        && StringValue is not null
        && comparandString.StartsWith(StringValue, stringComparison);

    /// <summary>
    /// Returns a value indicating whether this instance is a suffix of the specified <paramref name="other"/> instance.
    /// </summary>
    /// <param name="other">The other value to compare to this one.</param>
    /// <param name="stringComparison">The type of comparison if these are strings.</param>
    /// <returns><c>true</c> if the other value ends with this instance.</returns>
    public bool IsSuffixOf(object? other, StringComparison stringComparison) =>
        ToInvariantString(other) is { } comparandString
        && StringValue is not null
        && comparandString.EndsWith(StringValue, stringComparison);

    internal bool IsContainedByAsciiIgnoreCase(object? other) =>
        ToInvariantString(other) is { } comparandString
        && StringValue is not null
        && ToAsciiLowercase(comparandString).Contains(ToAsciiLowercase(StringValue), StringComparison.Ordinal);

    internal bool IsPrefixOfAsciiIgnoreCase(object? other) =>
        ToInvariantString(other) is { } comparandString
        && StringValue is not null
        && ToAsciiLowercase(comparandString).StartsWith(ToAsciiLowercase(StringValue), StringComparison.Ordinal);

    internal bool IsSuffixOfAsciiIgnoreCase(object? other) =>
        ToInvariantString(other) is { } comparandString
        && StringValue is not null
        && ToAsciiLowercase(comparandString).EndsWith(ToAsciiLowercase(StringValue), StringComparison.Ordinal);

    /// <summary>
    /// Determines whether the specified <paramref name="overrideValue"/> is an "exact" match for this instance.
    /// If this instance is an array, then it's checking to see if the value is in the array.
    /// </summary>
    /// <param name="overrideValue">The override value.</param>
    /// <returns><c>true</c> if the override value is an "exact" match for this value.</returns>
    public bool IsExactMatch(object? overrideValue)
    {
        if (TryGetBooleanValue(out var booleanValue))
        {
            return booleanValue == IsTruthyPropertyValue(overrideValue);
        }

        return this switch
        {
            { ListOfStrings: { } listOfStrings } => IsExactListMatch(listOfStrings, _numericListValues, overrideValue),
            { StringValue: { } stringValue } => UnicodeLowercaseEquals(stringValue, ToInvariantString(overrideValue)),
            _ => false
        };
    }

    bool TryGetBooleanValue(out bool value)
    {
        if (BooleanValue is { } booleanValue)
        {
            value = booleanValue;
            return true;
        }
        if (StringValue is { } stringValue && TryParseBoolean(stringValue, out value))
        {
            return true;
        }
        if (_booleanListValue is { } booleanListValue)
        {
            value = booleanListValue;
            return true;
        }

        value = false;
        return false;
    }

    static bool IsExactListMatch(
        IReadOnlyList<string> values,
        IReadOnlyList<string>? numericValues,
        object? overrideValue)
    {
        if (overrideValue is null)
        {
            return false;
        }

        var stringValue = ToInvariantString(overrideValue);
        if (stringValue is not null && values.Any(value => UnicodeLowercaseEquals(value, stringValue)))
        {
            return true;
        }

        if (numericValues is null)
        {
            return false;
        }

        return Type.GetTypeCode(overrideValue.GetType()) switch
        {
            TypeCode.Double => numericValues.Any(value =>
                TryParseDoubleWithoutUnderflow(value, out var number)
                && number.Equals((double)overrideValue)),
            TypeCode.Single => numericValues.Any(value =>
                TryParseSingleWithoutUnderflow(value, out var number)
                && number.Equals((float)overrideValue)),
            TypeCode.Byte or TypeCode.Decimal or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
                or TypeCode.SByte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
                => numericValues.Any(value =>
                    TryParseDecimalWithoutUnderflow(value, out var number)
                    && number == Convert.ToDecimal(overrideValue, CultureInfo.InvariantCulture)),
            _ => false
        };
    }

    // Override values use the same compact JSON representation as serde_json::Value::to_string in the flags service.
    // Strings remain unquoted because the service returns their contents directly.
    static string? ToInvariantString(object? value) =>
        ToInvariantString(value, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);

    static string? ToInvariantString(object? value, HashSet<object> ancestors, int depth) => value switch
    {
        null => "null",
        string stringValue => stringValue,
        char character => character.ToString(),
        bool booleanValue => booleanValue ? "true" : "false",
        double doubleValue => StringifyFloatingPoint(doubleValue),
        float floatValue => StringifyFloatingPoint(floatValue),
        JsonDocument document => StringifyJsonElement(document.RootElement),
        JsonElement element => StringifyJsonElement(element),
        IDictionary dictionary => StringifyDictionary(dictionary, ancestors, depth),
        IEnumerable enumerable => StringifyArray(enumerable, ancestors, depth),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    static string StringifyFloatingPoint(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value)
            ? StringifyFiniteFloatingPoint(value.ToString("R", CultureInfo.InvariantCulture))
            : value.ToString("R", CultureInfo.InvariantCulture);

    static string StringifyFloatingPoint(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value)
            ? StringifyFiniteFloatingPoint(value.ToString("R", CultureInfo.InvariantCulture))
            : value.ToString("R", CultureInfo.InvariantCulture);

    static string StringifyFiniteFloatingPoint(string roundTripValue)
    {
        var isNegative = roundTripValue.Length > 0 && roundTripValue[0] == '-';
        var unsignedValue = isNegative ? roundTripValue.Substring(1) : roundTripValue;
        var exponentMarker = unsignedValue.IndexOfAny(['E', 'e']);
        var significand = exponentMarker >= 0 ? unsignedValue.Substring(0, exponentMarker) : unsignedValue;
        var explicitExponent = exponentMarker >= 0
            ? ParseExponent(unsignedValue, exponentMarker + 1)
            : 0;
        var decimalPoint = significand.IndexOfAny(['.']);
        if (decimalPoint < 0)
        {
            decimalPoint = significand.Length;
        }

        var untrimmedDigits = decimalPoint < significand.Length
            ? significand.Remove(decimalPoint, 1)
            : significand;
        var firstNonZero = 0;
        while (firstNonZero < untrimmedDigits.Length && untrimmedDigits[firstNonZero] == '0')
        {
            firstNonZero++;
        }
        if (firstNonZero == untrimmedDigits.Length)
        {
            return isNegative ? "-0.0" : "0.0";
        }

        var exponent = explicitExponent + decimalPoint - firstNonZero - 1;
        var digits = untrimmedDigits.Substring(firstNonZero).TrimEnd('0');
        var sign = isNegative ? "-" : string.Empty;
        if (exponent <= -6 || exponent >= 16)
        {
            var fraction = digits.Length > 1 ? $".{digits.Substring(1)}" : string.Empty;
            var exponentSign = exponent >= 0 ? "+" : string.Empty;
            return $"{sign}{digits[0]}{fraction}e{exponentSign}{exponent}";
        }

        var output = new StringBuilder(sign);
        if (exponent < 0)
        {
            output.Append("0.");
            output.Append('0', -exponent - 1);
            output.Append(digits);
            return output.ToString();
        }

        var integerLength = exponent + 1;
        if (digits.Length <= integerLength)
        {
            output.Append(digits);
            output.Append('0', integerLength - digits.Length);
            output.Append(".0");
            return output.ToString();
        }

        output.Append(digits, 0, integerLength);
        output.Append('.');
        output.Append(digits, integerLength, digits.Length - integerLength);
        return output.ToString();
    }

    static int ParseExponent(string value, int startIndex)
    {
        var isNegative = value[startIndex] == '-';
        var index = value[startIndex] is '-' or '+' ? startIndex + 1 : startIndex;
        var exponent = 0;
        while (index < value.Length)
        {
            exponent = exponent * 10 + value[index] - '0';
            index++;
        }
        return isNegative ? -exponent : exponent;
    }

    static string StringifyJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number when element.TryGetInt64(out var integer) =>
            integer.ToString(CultureInfo.InvariantCulture),
        JsonValueKind.Number when element.TryGetUInt64(out var unsignedInteger) =>
            unsignedInteger.ToString(CultureInfo.InvariantCulture),
        JsonValueKind.Number => StringifyFloatingPoint(element.GetDouble()),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        JsonValueKind.Array => StringifyJsonArray(element),
        JsonValueKind.Object => StringifyJsonObject(element),
        _ => element.GetRawText()
    };

    static string StringifyJsonArray(JsonElement element)
    {
        var output = new StringBuilder("[");
        var first = true;
        foreach (var item in element.EnumerateArray())
        {
            if (!first)
            {
                output.Append(',');
            }
            AppendJsonValue(output, item);
            first = false;
        }
        return output.Append(']').ToString();
    }

    static string StringifyJsonObject(JsonElement element)
    {
        var properties = new SortedDictionary<string, JsonElement>(Utf8StringComparer.Instance);
        foreach (var property in element.EnumerateObject())
        {
            properties[property.Name] = property.Value;
        }

        var output = new StringBuilder("{");
        var first = true;
        foreach (var property in properties)
        {
            if (!first)
            {
                output.Append(',');
            }
            AppendJsonString(output, property.Key);
            output.Append(':');
            AppendJsonValue(output, property.Value);
            first = false;
        }
        return output.Append('}').ToString();
    }

    static string? StringifyDictionary(IDictionary dictionary, HashSet<object> ancestors, int depth)
    {
        if (depth >= 64 || !ancestors.Add(dictionary))
        {
            return null;
        }

        try
        {
            var properties = new SortedDictionary<string, object?>(Utf8StringComparer.Instance);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key)
                {
                    return null;
                }
                properties[key] = entry.Value;
            }

            var output = new StringBuilder("{");
            var first = true;
            foreach (var property in properties)
            {
                if (!first)
                {
                    output.Append(',');
                }
                AppendJsonString(output, property.Key);
                output.Append(':');
                if (!AppendJsonValue(output, property.Value, ancestors, depth + 1))
                {
                    return null;
                }
                first = false;
            }
            return output.Append('}').ToString();
        }
        finally
        {
            ancestors.Remove(dictionary);
        }
    }

    static string? StringifyArray(IEnumerable values, HashSet<object> ancestors, int depth)
    {
        if (depth >= 64 || !ancestors.Add(values))
        {
            return null;
        }

        try
        {
            var output = new StringBuilder("[");
            var first = true;
            foreach (var value in values)
            {
                if (!first)
                {
                    output.Append(',');
                }
                if (!AppendJsonValue(output, value, ancestors, depth + 1))
                {
                    return null;
                }
                first = false;
            }
            return output.Append(']').ToString();
        }
        finally
        {
            ancestors.Remove(values);
        }
    }

    static void AppendJsonValue(StringBuilder output, object? value)
    {
        if (value is string stringValue)
        {
            AppendJsonString(output, stringValue);
            return;
        }
        if (value is char character)
        {
            AppendJsonString(output, character.ToString());
            return;
        }
        if (value is JsonElement { ValueKind: JsonValueKind.String } stringElement)
        {
            AppendJsonString(output, stringElement.GetString() ?? string.Empty);
            return;
        }

        output.Append(ToInvariantString(value));
    }

    static bool AppendJsonValue(
        StringBuilder output,
        object? value,
        HashSet<object> ancestors,
        int depth)
    {
        if (value is string stringValue)
        {
            AppendJsonString(output, stringValue);
            return true;
        }
        if (value is char character)
        {
            AppendJsonString(output, character.ToString());
            return true;
        }
        if (value is JsonElement { ValueKind: JsonValueKind.String } stringElement)
        {
            AppendJsonString(output, stringElement.GetString() ?? string.Empty);
            return true;
        }
        if (value is JsonDocument { RootElement.ValueKind: JsonValueKind.String } stringDocument)
        {
            AppendJsonString(output, stringDocument.RootElement.GetString() ?? string.Empty);
            return true;
        }

        var stringifiedValue = ToInvariantString(value, ancestors, depth);
        if (stringifiedValue is null)
        {
            return false;
        }
        output.Append(stringifiedValue);
        return true;
    }

    static void AppendJsonString(StringBuilder output, string value)
    {
        output.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                case '\b': output.Append("\\b"); break;
                case '\t': output.Append("\\t"); break;
                case '\n': output.Append("\\n"); break;
                case '\f': output.Append("\\f"); break;
                case '\r': output.Append("\\r"); break;
                case < ' ':
                    output.Append("\\u");
                    output.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default: output.Append(character); break;
            }
        }
        output.Append('"');
    }

    static bool TryParseBoolean(string value, out bool result)
    {
        var lowercaseValue = UnicodeLowercase(value);
        if (lowercaseValue == "true")
        {
            result = true;
            return true;
        }
        if (lowercaseValue == "false")
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    static bool? TryGetBooleanListValue(IEnumerable<string> values)
    {
        var result = true;
        foreach (var value in values)
        {
            if (!TryParseBoolean(value, out var booleanValue))
            {
                return null;
            }
            result &= booleanValue;
        }
        return result;
    }

    static bool IsTruthyPropertyValue(object? value) =>
        IsTruthyPropertyValue(value, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);

    static bool IsTruthyPropertyValue(object? value, HashSet<object> ancestors, int depth) => value switch
    {
        bool booleanValue => booleanValue,
        string stringValue => UnicodeLowercase(stringValue) == "true",
        JsonDocument document => IsTruthyJsonValue(document.RootElement, depth),
        JsonElement element => IsTruthyJsonValue(element, depth),
        IDictionary => false,
        IEnumerable enumerable => AllTruthy(enumerable, ancestors, depth),
        _ => false
    };

    static bool IsTruthyJsonValue(JsonElement value, int depth) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.String => UnicodeLowercase(value.GetString() ?? string.Empty) == "true",
        JsonValueKind.Array when depth < 64 =>
            value.EnumerateArray().All(element => IsTruthyJsonValue(element, depth + 1)),
        _ => false
    };

    static bool AllTruthy(IEnumerable values, HashSet<object> ancestors, int depth)
    {
        if (depth >= 64 || !ancestors.Add(values))
        {
            return false;
        }

        try
        {
            foreach (var value in values)
            {
                if (!IsTruthyPropertyValue(value, ancestors, depth + 1))
                {
                    return false;
                }
            }
            return true;
        }
        finally
        {
            ancestors.Remove(values);
        }
    }

#pragma warning disable CA1308 // The flags service lowercases both operands; uppercasing has different Unicode semantics.
    static bool UnicodeLowercaseEquals(string left, string? right) =>
        right is not null
        && string.Equals(UnicodeLowercase(left), UnicodeLowercase(right), StringComparison.Ordinal);

    // .NET's invariant mapping does not expand dotted-I or apply the exact Unicode Final_Sigma context used by Rust.
    // UnicodeSpecialCasingData supplies the exact derived properties used
    // by that condition from Unicode 17.0, the version used by the current flags service Rust toolchain.
    static string UnicodeLowercase(string value)
    {
        var expanded = new StringBuilder(value.Length + 1);
        for (var index = 0; index < value.Length;)
        {
            var codePoint = GetCodePoint(value, index, out var codePointLength);
            if (codePoint == 0x0130)
            {
                expanded.Append("i\u0307");
            }
            else if (codePoint == 0x03A3)
            {
                expanded.Append(IsFinalSigma(value, index, codePointLength) ? '\u03C2' : '\u03C3');
            }
            else
            {
                expanded.Append(value, index, codePointLength);
            }
            index += codePointLength;
        }
        return expanded.ToString().ToLowerInvariant();
    }

    static bool IsFinalSigma(string value, int index, int codePointLength) =>
        HasCasedCodePointBefore(value, index)
        && !HasCasedCodePointAfter(value, index + codePointLength);

    static bool HasCasedCodePointBefore(string value, int index)
    {
        while (index > 0)
        {
            var codePoint = GetPreviousCodePoint(value, ref index);
            if (UnicodeSpecialCasingData.IsCased(codePoint))
            {
                return true;
            }
            if (!UnicodeSpecialCasingData.IsCaseIgnorable(codePoint))
            {
                return false;
            }
        }
        return false;
    }

    static bool HasCasedCodePointAfter(string value, int index)
    {
        while (index < value.Length)
        {
            var codePoint = GetCodePoint(value, index, out var codePointLength);
            if (UnicodeSpecialCasingData.IsCased(codePoint))
            {
                return true;
            }
            if (!UnicodeSpecialCasingData.IsCaseIgnorable(codePoint))
            {
                return false;
            }
            index += codePointLength;
        }
        return false;
    }

    static int GetCodePoint(string value, int index, out int length)
    {
        if (char.IsHighSurrogate(value[index])
            && index + 1 < value.Length
            && char.IsLowSurrogate(value[index + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(value[index], value[index + 1]);
        }

        length = 1;
        return value[index];
    }

    static int GetPreviousCodePoint(string value, ref int index)
    {
        index--;
        if (index > 0 && char.IsLowSurrogate(value[index]) && char.IsHighSurrogate(value[index - 1]))
        {
            index--;
            return char.ConvertToUtf32(value[index], value[index + 1]);
        }
        return value[index];
    }
#pragma warning restore CA1308

    static string ToAsciiLowercase(string value)
    {
        var characters = value.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (characters[index] is >= 'A' and <= 'Z')
            {
                characters[index] = (char)(characters[index] + ('a' - 'A'));
            }
        }
        return new string(characters);
    }

    static bool TryParseDoubleWithoutUnderflow(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && (number != 0 || RepresentsZero(value));

    static bool TryParseSingleWithoutUnderflow(string value, out float number) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && (number != 0 || RepresentsZero(value));

    static bool TryParseDecimalWithoutUnderflow(string value, out decimal number) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && (number != 0 || RepresentsZero(value));

    static bool RepresentsZero(string value)
    {
        foreach (var character in value)
        {
            if (character is 'e' or 'E')
            {
                break;
            }
            if (character is >= '1' and <= '9')
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Compares this instance with the specified <paramref name="overrideValue"/> instance and indicates whether this
    /// instance precedes, follows, or appears in the same position in the sort order as the specified instance.
    /// Less than zero: This instance precedes <paramref name="overrideValue"/> in the sort order.
    /// Zero: This instance appears in the same position in the sort order as <paramref name="overrideValue"/>.
    /// Greater than zero: This instance follows <paramref name="overrideValue"/> in the sort order or other is null.
    /// </summary>
    /// <remarks>
    /// For string values, does a case-insensitive comparison.
    /// </remarks>
    /// <param name="overrideValue">The <see cref="PropertyFilterValue"/> to compare with.</param>
    /// <returns>
    /// A value that indicates the relative order of the objects being compared. The return value has these meanings:
    /// 0: This instance and <paramref name="overrideValue"/> are equal.
    /// -1: This instance precedes <paramref name="overrideValue"/> in the sort order.
    /// 1: This instance follows <paramref name="overrideValue"/> in the sort order.
    /// </returns>
    public int CompareTo(object? overrideValue)
    {
        if (ReferenceEquals(overrideValue, null))
        {
            return 1;
        }

        return overrideValue switch
        {
            _ when TryCompareNumbers(overrideValue, out var result) => result.Value,
            _ when BooleanValue.HasValue => CompareBooleanValue(overrideValue),
            _ => string.Compare(StringValue, ToInvariantString(overrideValue), StringComparison.OrdinalIgnoreCase)
        };
    }

    bool TryCompareNumbers(object overrideValue, [NotNullWhen(returnValue: true)] out int? result)
    {
        if (!double.TryParse(StringValue, out var doubleValue))
        {
            result = null;
            return false;
        }

        result = overrideValue switch
        {
            double overrideDouble => doubleValue.CompareTo(overrideDouble),
            long overrideLong => doubleValue.CompareTo(overrideLong),
            int overrideInt => doubleValue.CompareTo(overrideInt),
            string overrideString when double.TryParse(overrideString, out var doubleOverrideValue) => doubleValue.CompareTo(doubleOverrideValue),
            _ => null
        };
        return result is not null;
    }

    int CompareBooleanValue(object overrideValue)
    {
        if (!BooleanValue.HasValue)
        {
            return -1;
        }

        return overrideValue switch
        {
            bool boolOverride => BooleanValue.Value.CompareTo(boolOverride),
            string stringOverride when bool.TryParse(stringOverride, out var boolValue) => BooleanValue.Value.CompareTo(boolValue),
            _ => string.Compare(BooleanValue.Value.ToString(), ToInvariantString(overrideValue), StringComparison.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Determines whether the override value represents a date before the date represented by this instance.
    /// </summary>
    /// <param name="overrideValue">The supplied override value.</param>
    /// <param name="now">The current date.</param>
    /// <returns><c>true</c> if the override date value is before the date represented by the filter value.</returns>
    /// <exception cref="InconclusiveMatchException">Thrown if the filter value can't be parsed.</exception>
    public bool IsDateBefore(object? overrideValue, DateTimeOffset now)
    {
        // Question: Should we support DateOnly and TimeOnly?
        var overrideDate = ParseDate(overrideValue);

        if (RelativeDate.TryParseRelativeDate(StringValue, out var relativeDate))
        {
            return overrideDate is DateTimeOffset overrideDateTimeOffset && relativeDate.IsDateBefore(overrideDateTimeOffset, now)
                   || (overrideDate is DateTime overrideDateTime && relativeDate.IsDateBefore(overrideDateTime, now));
        }

        return ParseDate(StringValue) switch
        {
            DateTimeOffset comparandDate => overrideDate is DateTimeOffset overrideDateTimeOffset && overrideDateTimeOffset < comparandDate,
            DateTime comparandDate => overrideDate is DateTimeOffset overrideDateTimeOffset && overrideDateTimeOffset < comparandDate,
            _ => throw new InconclusiveMatchException("The date provided is not a valid format")
        };

        object? ParseDate(object? value)
        {
            if (value is DateTime or DateTimeOffset
#if NET5_0_OR_GREATER
                or DateOnly
#endif
)
            {
                return value;
            }

            return value is string dateString
                ? DateTimeOffset.TryParse(dateString, out var dto)
                    ? dto
                    : DateTime.TryParse(dateString, out var dt)
                        ? dt
                        : throw new InconclusiveMatchException("The date provided is not a valid format")
                : throw new InconclusiveMatchException("The date provided must be a string, DateTime, or DateTimeOffset, object");
        }
    }

    /// <summary>
    /// Determines whether the left filter value is greater than the right value.
    /// </summary>
    /// <param name="left">The filter value to compare.</param>
    /// <param name="right">The value to compare against.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is greater than <paramref name="right"/>; otherwise <c>false</c>.</returns>
    public static bool operator >(PropertyFilterValue left, object? right) => NotNull(left).CompareTo(right) > 0;

    /// <summary>
    /// Determines whether the left filter value is less than the right value.
    /// </summary>
    /// <param name="left">The filter value to compare.</param>
    /// <param name="right">The value to compare against.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is less than <paramref name="right"/>; otherwise <c>false</c>.</returns>
    public static bool operator <(PropertyFilterValue? left, object? right) => NotNull(left).CompareTo(right) < 0;

    /// <summary>
    /// Determines whether the left filter value is greater than or equal to the right value.
    /// </summary>
    /// <param name="left">The filter value to compare.</param>
    /// <param name="right">The value to compare against.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>; otherwise <c>false</c>.</returns>
    public static bool operator >=(PropertyFilterValue left, object? right) => NotNull(left).CompareTo(right) >= 0;

    /// <summary>
    /// Determines whether the left filter value is less than or equal to the right value.
    /// </summary>
    /// <param name="left">The filter value to compare.</param>
    /// <param name="right">The value to compare against.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is less than or equal to <paramref name="right"/>; otherwise <c>false</c>.</returns>
    public static bool operator <=(PropertyFilterValue left, object? right) => NotNull(left).CompareTo(right) <= 0;

    static SemanticVersion ParseOverrideSemver(object? overrideValue)
    {
        var overrideVersionString = ToInvariantString(overrideValue);
        if (!SemanticVersion.TryParse(overrideVersionString, out var version))
        {
            throw new InconclusiveMatchException($"Cannot parse override value '{overrideVersionString}' as a semantic version");
        }
        return version.Value;
    }

    SemanticVersion ParseFilterSemver()
    {
        if (!SemanticVersion.TryParse(StringValue, out var version))
        {
            throw new InconclusiveMatchException($"Cannot parse filter value '{StringValue}' as a semantic version");
        }
        return version.Value;
    }

    /// <summary>
    /// Compares the override value as a semantic version against this filter value.
    /// </summary>
    /// <param name="overrideValue">The version value from person/group properties.</param>
    /// <returns>A comparison result: negative if override &lt; filter, zero if equal, positive if override &gt; filter.</returns>
    /// <exception cref="InconclusiveMatchException">Thrown if either value cannot be parsed as a valid semver.</exception>
    public int CompareSemver(object? overrideValue)
    {
        var overrideVersion = ParseOverrideSemver(overrideValue);
        var filterVersion = ParseFilterSemver();
        return overrideVersion.CompareTo(filterVersion);
    }

    /// <summary>
    /// Checks if the override value is within the tilde range specified by this filter value.
    /// ~X.Y.Z means >=X.Y.Z and &lt;X.Y+1.0
    /// </summary>
    /// <param name="overrideValue">The version value from person/group properties.</param>
    /// <returns><c>true</c> if the override version is within the tilde range.</returns>
    /// <exception cref="InconclusiveMatchException">Thrown if either value cannot be parsed as a valid semver.</exception>
    public bool IsSemverTildeMatch(object? overrideValue)
    {
        var overrideVersion = ParseOverrideSemver(overrideValue);
        var filterVersion = ParseFilterSemver();
        var (lower, upper) = filterVersion.GetTildeBounds();
        return overrideVersion.IsInRange(lower, upper);
    }

    /// <summary>
    /// Checks if the override value is within the caret range specified by this filter value.
    /// ^X.Y.Z is compatible-with per semver spec:
    /// - ^1.2.3 means >=1.2.3 &lt;2.0.0 (major > 0)
    /// - ^0.2.3 means >=0.2.3 &lt;0.3.0 (major = 0, minor > 0)
    /// - ^0.0.3 means >=0.0.3 &lt;0.0.4 (major = 0, minor = 0)
    /// </summary>
    /// <param name="overrideValue">The version value from person/group properties.</param>
    /// <returns><c>true</c> if the override version is within the caret range.</returns>
    /// <exception cref="InconclusiveMatchException">Thrown if either value cannot be parsed as a valid semver.</exception>
    public bool IsSemverCaretMatch(object? overrideValue)
    {
        var overrideVersion = ParseOverrideSemver(overrideValue);
        var filterVersion = ParseFilterSemver();
        var (lower, upper) = filterVersion.GetCaretBounds();
        return overrideVersion.IsInRange(lower, upper);
    }

    /// <summary>
    /// Checks if the override value matches the wildcard pattern specified by this filter value.
    /// "X.*" or "X" means >=X.0.0 &lt;X+1.0.0
    /// "X.Y.*" means >=X.Y.0 &lt;X.Y+1.0
    /// </summary>
    /// <param name="overrideValue">The version value from person/group properties.</param>
    /// <returns><c>true</c> if the override version matches the wildcard pattern.</returns>
    /// <exception cref="InconclusiveMatchException">Thrown if either value cannot be parsed.</exception>
    public bool IsSemverWildcardMatch(object? overrideValue)
    {
        var overrideVersion = ParseOverrideSemver(overrideValue);

        if (!TryParseWildcard(StringValue, out var lower, out var upper))
        {
            throw new InconclusiveMatchException($"Cannot parse filter value '{StringValue}' as a wildcard pattern");
        }

        return overrideVersion.IsInRange(lower.Value, upper.Value);
    }

    /// <summary>
    /// Returns a string representation of this filter value.
    /// </summary>
    /// <returns>The string, cohort, list, or boolean value represented as a string.</returns>
    public override string ToString()
    {
        return this switch
        {
            { StringValue: { } stringValue } => stringValue,
            { CohortId: { } cohortId } => cohortId.ToString(CultureInfo.InvariantCulture),
            { ListOfStrings: { } listOfStrings } => $"[{string.Join(", ", listOfStrings)}]",
#pragma warning disable CA1308
            { BooleanValue: { } booleanValue } => booleanValue.ToString().ToLowerInvariant(),
#pragma warning restore CA1308
            _ => string.Empty
        };
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current filter value.
    /// </summary>
    /// <param name="obj">The object to compare with the current filter value.</param>
    /// <returns><c>true</c> if the specified object is equal to the current filter value; otherwise <c>false</c>.</returns>
    public override bool Equals(object? obj) =>
        obj is PropertyFilterValue other
        && Equals(other);

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current filter value.</returns>
    public override int GetHashCode() => HashCode.Combine(StringValue, ListOfStrings, _numericListValues, BooleanValue);

    /// <summary>
    /// Determines if this instance is equal to the specified <paramref name="other"/> <see cref="PropertyFilterValue"/>
    /// instance. This should not be used when evaluating filter property conditions.
    /// </summary>
    /// <param name="other">The <see cref="PropertyFilterValue"/> to compare to.</param>
    /// <returns><c>true</c> if these represent the same filter property value.</returns>
    public bool Equals(PropertyFilterValue? other)
    {
        if (ReferenceEquals(other, null))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return ListOfStrings.ListsAreEqual(other.ListOfStrings)
               && _numericListValues.ListsAreEqual(other._numericListValues)
               && StringValue == other.StringValue
               && CohortId == other.CohortId
               && BooleanValue == other.BooleanValue;
    }

    sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static ReferenceEqualityComparer Instance { get; } = new();

        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);

        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }

    sealed class Utf8StringComparer : IComparer<string>
    {
        internal static Utf8StringComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }
            if (left is null)
            {
                return -1;
            }
            if (right is null)
            {
                return 1;
            }

            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            var sharedLength = Math.Min(leftBytes.Length, rightBytes.Length);
            for (var index = 0; index < sharedLength; index++)
            {
                var comparison = leftBytes[index].CompareTo(rightBytes[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }
            return leftBytes.Length.CompareTo(rightBytes.Length);
        }
    }

    static bool TryParseStringArray(
        JsonElement jsonElement,
        [NotNullWhen(returnValue: true)] out IReadOnlyList<string>? value,
        out IReadOnlyList<string>? numericValues,
        out bool? booleanValue)
    {
        List<string> values = [];
        List<string> numbers = [];
        foreach (var element in jsonElement.EnumerateArray())
        {
            var stringValue = element.ValueKind is JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : StringifyJsonElement(element);
            values.Add(stringValue);
            if (element.ValueKind is JsonValueKind.Number)
            {
                numbers.Add(stringValue);
            }
        }

        value = values.ToReadOnlyList();
        numericValues = numbers.Count > 0 ? numbers.ToReadOnlyList() : null;
        booleanValue = TryGetJsonBooleanValue(jsonElement);
        return true;
    }

    static bool? TryGetJsonBooleanValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when TryParseBoolean(value.GetString() ?? string.Empty, out var booleanValue)
            => booleanValue,
        JsonValueKind.Array => TryGetJsonBooleanArrayValue(value),
        _ => null
    };

    static bool? TryGetJsonBooleanArrayValue(JsonElement value)
    {
        var result = true;
        foreach (var element in value.EnumerateArray())
        {
            if (TryGetJsonBooleanValue(element) is not { } booleanValue)
            {
                return null;
            }
            result &= booleanValue;
        }
        return result;
    }
}