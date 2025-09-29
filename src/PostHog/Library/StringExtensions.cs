using System;
using System.Collections.Generic;
using System.Text;

namespace PostHog.Library;

internal static class StringExtensions
{
    /// <summary>
    /// Truncates <paramref name="value"/> so its UTF-8 byte length is at most <paramref name="maxLength"/>.
    /// Appends <paramref name="truncationSymbol"/> if truncation occurs. Respects UTF-8 code point boundaries, but it can split graphemes.
    /// </summary>
    /// <param> name="value">The string to truncate.</param>
    /// <param name="maxLength">Maximum allowed byte length in UTF-8 encoding.</param>
    /// <param name="truncationSymbol">Optional: The symbol to append if truncation occurs. Default is "…".</param>
    /// <returns>The truncated string, if longer than <paramref name="maxLength"/>, otherwise original <paramref name="value"/>.</returns>
    public static string TruncateByBytes(this string value, int maxLength, string truncationSymbol = "…")
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (maxLength <= 0) return string.Empty;

        var utf8 = Encoding.UTF8;
        int totalBytes = utf8.GetByteCount(value);

        if (totalBytes <= maxLength) return value;

        int truncationSymbolBytes = utf8.GetByteCount(truncationSymbol);

        if (truncationSymbolBytes > maxLength)
        {
            return string.Empty;
        }

        int cut = maxLength - truncationSymbolBytes;
        byte[] bytes = utf8.GetBytes(value);

        // back up to the start of a code point (avoid inside a continuation byte 10xxxxxx)
        while (cut > 0 && (bytes[cut] & 0b1100_0000) == 0b1000_0000)
        {
            cut--;
        }

        string head = utf8.GetString(bytes, 0, cut);
        return head + truncationSymbol;
    }

    /// <summary>
    /// Truncates <paramref name="value"/> so it is at most <paramref name="maxLength"/> characters.
    /// Appends <paramref name="truncationSymbol"/> if truncation occurs.
    /// </summary>
    /// <param> name="value">The string to truncate.</param>
    /// <param name="maxLength">Maximum allowed characters.</param>
    /// <param name="truncationSymbol">Optional: The symbol to append if truncation occurs. Default is "…".</param>
    /// <returns>The truncated string, if longer than <paramref name="maxLength"/>, otherwise original <paramref name="value"/>.</returns>
    public static string TruncateByCharacters(this string value, int maxLength, string truncationSymbol = "…")
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
        if (maxLength <= 0 || truncationSymbol.Length >= maxLength) return string.Empty;

        return value[..(maxLength - truncationSymbol.Length)] + truncationSymbol;
    }
}
