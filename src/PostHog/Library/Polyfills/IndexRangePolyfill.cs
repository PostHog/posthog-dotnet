#if NETSTANDARD2_0
using System.Runtime.CompilerServices;

// ReSharper disable once CheckNamespace
namespace System;

/// <summary>
/// Polyfill for System.Index to enable C# 8 index syntax (e.g., ^1) on netstandard2.0.
/// </summary>
internal readonly struct Index : IEquatable<Index>
{
    readonly int _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Index(int value, bool fromEnd = false)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "value must be non-negative");
        }

        _value = fromEnd ? ~value : value;
    }

    Index(int value)
    {
        _value = value;
    }

    public static Index Start => new(0);
    public static Index End => new(~0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Index FromStart(int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "value must be non-negative");
        }

        return new Index(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Index FromEnd(int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "value must be non-negative");
        }

        return new Index(~value);
    }

    public int Value => _value < 0 ? ~_value : _value;
    public bool IsFromEnd => _value < 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetOffset(int length)
    {
        var offset = _value;
        if (IsFromEnd)
        {
            offset += length + 1;
        }

        return offset;
    }

    public override bool Equals(object? value) => value is Index index && _value == index._value;
    public bool Equals(Index other) => _value == other._value;
    public override int GetHashCode() => _value;

    public static implicit operator Index(int value) => FromStart(value);
}

/// <summary>
/// Polyfill for System.Range to enable C# 8 range syntax (e.g., 1..^1) on netstandard2.0.
/// </summary>
internal readonly struct Range : IEquatable<Range>
{
    public Index Start { get; }
    public Index End { get; }

    public Range(Index start, Index end)
    {
        Start = start;
        End = end;
    }

    public static Range StartAt(Index start) => new(start, Index.End);
    public static Range EndAt(Index end) => new(Index.Start, end);
    public static Range All => new(Index.Start, Index.End);

    public override bool Equals(object? value) => value is Range r && r.Start.Equals(Start) && r.End.Equals(End);
    public bool Equals(Range other) => other.Start.Equals(Start) && other.End.Equals(End);
    public override int GetHashCode() => Start.GetHashCode() * 31 + End.GetHashCode();

    public (int Offset, int Length) GetOffsetAndLength(int length)
    {
        var start = Start.GetOffset(length);
        var end = End.GetOffset(length);
        if ((uint)end > (uint)length || (uint)start > (uint)end)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return (start, end - start);
    }
}

internal static class RuntimeCompatibilityExtensions
{
    public static string Substring(this string s, Range range)
    {
        var (offset, length) = range.GetOffsetAndLength(s.Length);
        return s.Substring(offset, length);
    }
}
#endif
