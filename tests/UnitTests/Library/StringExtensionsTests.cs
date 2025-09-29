using PostHog.Library;
using System;
using System.Collections.Generic;
using System.Text;

namespace StringExtensionsTests;

public class TheTruncateByBytesMethod
{
    private static readonly Encoding Utf8Strict =
        Encoding.GetEncoding("utf-8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    [Theory]
    [InlineData("", 10, "")]
    public void ReturnsOriginalWhenEmptyString(string input, int max, string expected)
        => Assert.Equal(expected, StringExtensions.TruncateByBytes(input, max));

    [Fact]
    public void ReturnsOriginalWhenFits()
    {
        var s = "Hello";
        var res = StringExtensions.TruncateByBytes(s, Encoding.UTF8.GetByteCount(s));
        Assert.Equal(s, res);
    }

    [Fact]
    public void MaxLengthZeroOrLessReturnsEmpty()
    {
        Assert.Equal(string.Empty, StringExtensions.TruncateByBytes("X", 0));
        Assert.Equal(string.Empty, StringExtensions.TruncateByBytes("X", -5));
    }

    [Fact]
    public void TruncationSymbolDoesNotFitReturnsEmpty()
    {
        Assert.Equal(string.Empty, StringExtensions.TruncateByBytes("Hello", 2, "…"));
        Assert.Equal(string.Empty, StringExtensions.TruncateByBytes("Hello", 1, "…"));
    }

    [Fact]
    public void TruncatesAndAppendsEllipsis()
    {
        var res = StringExtensions.TruncateByBytes("Hello!", 5, "…");
        Assert.Equal("He…", res);
        Assert.True(Encoding.UTF8.GetByteCount(res) <= 5);
        // Must be strictly encodable (no unpaired surrogates)
        Utf8Strict.GetBytes(res);
    }

    [Fact]
    public void DoesNotSplitUtf8CodePointSurrogateWithPairEmoji()
    {
        var emoji = "🙂"; // U+1F642 (4 bytes in UTF-8)
        var s = emoji + emoji;
        var max = Encoding.UTF8.GetByteCount(emoji) + Encoding.UTF8.GetByteCount("…");

        var res = StringExtensions.TruncateByBytes(s, max, "…");

        Assert.Equal(emoji + "…", res);
        Assert.Equal(max, Encoding.UTF8.GetByteCount(res));
        Utf8Strict.GetBytes(res);
    }

    [Fact]
    public void DoesNotProduceInvalidUtf8WithCjk()
    {
        var s = "汉字测试";
        // allow exactly 1 CJK char + ellipsis
        var one = "汉";
        var max = Encoding.UTF8.GetByteCount(one) + Encoding.UTF8.GetByteCount("…");

        var res = StringExtensions.TruncateByBytes(s, max, "…");

        Assert.Equal(one + "…", res);
        Assert.Equal(max, Encoding.UTF8.GetByteCount(res));
        Utf8Strict.GetBytes(res);
    }

    [Fact]
    public void CombiningMarksTruncateToOneSequencePlusEllipsis()
    {
        // ééX -> bytes: [e(1)] + [U+0301(2)] + [e(1)] + [U+0301(2)] + [X(1)] = 7
        var s = "e\u0301e\u0301X";
        var oneSeq = "e\u0301"; // 3 bytes
        var max = Encoding.UTF8.GetByteCount(oneSeq) + Encoding.UTF8.GetByteCount("…"); // 6 bytes

        var res = StringExtensions.TruncateByBytes(s, max, "…");

        Assert.Equal(oneSeq + "…", res);
        Assert.Equal(max, Encoding.UTF8.GetByteCount(res));
        Encoding.GetEncoding("utf-8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetBytes(res);
    }

    [Fact]
    public void CombiningMarksTooTightBudgetDropsCombiningMark()
    {
        // éé -> 6 bytes total; set max=5 so head bytes = 2 before ellipsis
        var s = "e\u0301e\u0301";
        var max = 5;

        var res = StringExtensions.TruncateByBytes(s, max, "…");

        // Backs up to a UTF-8 code point boundary, so we keep just 'e' (1 byte) then ellipsis.
        Assert.Equal("e…", res);
        Assert.True(Encoding.UTF8.GetByteCount(res) <= max);
        Encoding.GetEncoding("utf-8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetBytes(res);
    }

    [Fact]
    public void WorksWithCustomEllipsis()
    {
        var s = "HelloWorld";
        var res = StringExtensions.TruncateByBytes(s, 7, "..?");
        Assert.Equal("Hell..?", res);
        Assert.True(Encoding.UTF8.GetByteCount(res) <= 7);
        Utf8Strict.GetBytes(res);
    }
}

public class TheTruncateByCharactersMethod
{
    [Theory]
    [InlineData("", 5, "")]
    public void ReturnsOriginalWhenEmptyString(string input, int max, string expected)
        => Assert.Equal(expected, StringExtensions.TruncateByCharacters(input, max));

    [Fact]
    public void ReturnsOriginalWhenFits()
    {
        Assert.Equal("Hello", StringExtensions.TruncateByCharacters("Hello", 5));
        Assert.Equal("Hi", StringExtensions.TruncateByCharacters("Hi", 10));
    }

    [Fact]
    public void MaxLengthZeroOrLessReturnsEmpty()
    {
        Assert.Equal(string.Empty, StringExtensions.TruncateByCharacters("X", 0));
        Assert.Equal(string.Empty, StringExtensions.TruncateByCharacters("X", -3));
    }

    [Fact]
    public void TruncationSymbolDoesNotFitReturnsEmpty()
    {
        Assert.Equal(string.Empty, StringExtensions.TruncateByCharacters("Hello", 1, "…"));
        Assert.Equal(string.Empty, StringExtensions.TruncateByCharacters("Hello", 3, "..."));
    }

    [Fact]
    public void UnicodeEllipsisConsumesOneCharacter()
    {
        var res = StringExtensions.TruncateByCharacters("Hello!", 5, "…");
        Assert.Equal("Hell…", res);
    }

    [Fact]
    public void AsciiEllipsisConsumesMoreCharacters()
    {
        var res = StringExtensions.TruncateByCharacters("Hello!", 5, "...");
        Assert.Equal("He...", res);
    }

    [Fact]
    public void MultiByteEmojiCountsAsTwoCodeUnits()
    {
        var s = "🙂🙂"; // Length 4 (two surrogate pairs)
        var res = StringExtensions.TruncateByCharacters(s, 3, "…");
        Assert.Equal(3, res.Length);
        Assert.Equal("🙂…", res);
    }

    [Fact]
    public void WorksWithCustomEllipsis()
    {
        var res = StringExtensions.TruncateByCharacters("abcdefghijk", 6, "..");
        Assert.Equal("abcd..", res);
    }
}
