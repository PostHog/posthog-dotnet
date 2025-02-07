#if NETSTANDARD2_0 || NETSTANDARD2_1
using System.Diagnostics;
using System.Runtime.CompilerServices;
using static PostHog.Library.Ensure;

namespace PostHog.Library.Polyfills;

internal static class Convert
{
    public static string ToHexString(byte[] inArray)
    {
        NotNull(inArray);

        return BitConverter.ToString(inArray).Replace("-", string.Empty);
    }

    // Parses value in base fromBase.  fromBase can only
    // be 2, 8, 10, or 16.  If fromBase is 16, the number may be preceded
    // by 0x; any other leading or trailing characters cause an error.
    //
    public static ulong ToUInt64(string? value, int fromBase)
    {
        if (fromBase != 2 && fromBase != 8 && fromBase != 10 && fromBase != 16)
        {
            throw new ArgumentException("Invalid base", nameof(fromBase));
        }
        return value != null ?
            (ulong)ParseNumbers.StringToLong(value.AsSpan(), fromBase, ParseNumbers.TreatAsUnsigned | ParseNumbers.IsTight) :
            0;
    }
}

/// <summary>Methods for parsing numbers and strings.</summary>
internal static class ParseNumbers
{
    internal const int TreatAsUnsigned = 0x0200;
    internal const int TreatAsI1 = 0x0400;
    internal const int TreatAsI2 = 0x0800;
    internal const int IsTight = 0x1000;

    public static long StringToLong(ReadOnlySpan<char> s, int radix, int flags)
    {
        int pos = 0;
        return StringToLong(s, radix, flags, ref pos);
    }

    public static long StringToLong(ReadOnlySpan<char> s, int radix, int flags, ref int currPos)
    {
        int i = currPos;

        // Do some radix checking.
        // A radix of -1 says to use whatever base is spec'd on the number.
        // Parse in Base10 until we figure out what the base actually is.
        int r = (-1 == radix) ? 10 : radix;

        if (r != 2 && r != 10 && r != 8 && r != 16)
            throw new ArgumentException("Invalid base", nameof(radix));

        int length = s.Length;

        if (i < 0 || i >= length)
            throw new ArgumentOutOfRangeException(nameof(i), "Index was out of range. Must be non-negative and less than the size of the collection.");

        // Get rid of the whitespace and then check that we've still got some digits to parse.
        if ((flags & IsTight) == 0)
        {
            EatWhiteSpace(s, ref i);
            if (i == length)
                throw new FormatException("Input string was not in a correct format.");
        }

        // Check for a sign
        int sign = 1;
        if (s[i] == '-')
        {
            if (r != 10)
                throw new ArgumentException("Cannot have negative value", nameof(radix));

            if ((flags & TreatAsUnsigned) != 0)
                throw new OverflowException("Negative unsigned");

            sign = -1;
            i++;
        }
        else if (s[i] == '+')
        {
            i++;
        }

        if ((radix == -1 || radix == 16) && (i + 1 < length) && s[i] == '0')
        {
            if (s[i + 1] == 'x' || s[i + 1] == 'X')
            {
                r = 16;
                i += 2;
            }
        }

        int grabNumbersStart = i;
        long result = GrabLongs(r, s, ref i, (flags & TreatAsUnsigned) != 0);

        // Check if they passed us a string with no parsable digits.
        if (i == grabNumbersStart)
            throw new FormatException("No parsible digits");

        if ((flags & IsTight) != 0)
        {
            // If we've got effluvia left at the end of the string, complain.
            if (i < length)
                throw new FormatException("Extra junk at end");
        }

        // Put the current index back into the correct place.
        currPos = i;

        // Return the value properly signed.
        if ((ulong)result == 0x8000000000000000 && sign == 1 && r == 10 && ((flags & TreatAsUnsigned) == 0))
            throw new OverflowException();

        if (r == 10)
        {
            result *= sign;
        }

        return result;
    }

    public static int StringToInt(ReadOnlySpan<char> s, int radix, int flags)
    {
        int pos = 0;
        return StringToInt(s, radix, flags, ref pos);
    }

    public static int StringToInt(ReadOnlySpan<char> s, int radix, int flags, ref int currPos)
    {
        // They're requied to tell me where to start parsing.
        int i = currPos;

        // Do some radix checking.
        // A radix of -1 says to use whatever base is spec'd on the number.
        // Parse in Base10 until we figure out what the base actually is.
        int r = (-1 == radix) ? 10 : radix;

        if (r != 2 && r != 10 && r != 8 && r != 16)
            throw new ArgumentException("Invalid base", nameof(radix));

        int length = s.Length;

        if (i < 0 || i >= length)
            throw new ArgumentOutOfRangeException(nameof(i), "Index was out of range. Must be non-negative and less than the size of the collection.");

        // Get rid of the whitespace and then check that we've still got some digits to parse.
        if ((flags & IsTight) == 0)
        {
            EatWhiteSpace(s, ref i);
            if (i == length)
                throw new FormatException("Input string was not in a correct format.");
        }

        // Check for a sign
        int sign = 1;
        if (s[i] == '-')
        {
            if (r != 10)
                throw new ArgumentException("Cannot have negative value", nameof(radix));

            if ((flags & TreatAsUnsigned) != 0)
                throw new OverflowException("Negative unsigned");

            sign = -1;
            i++;
        }
        else if (s[i] == '+')
        {
            i++;
        }

        // Consume the 0x if we're in an unknown base or in base-16.
        if ((radix == -1 || radix == 16) && (i + 1 < length) && s[i] == '0')
        {
            if (s[i + 1] == 'x' || s[i + 1] == 'X')
            {
                r = 16;
                i += 2;
            }
        }

        int grabNumbersStart = i;
        int result = GrabInts(r, s, ref i, (flags & TreatAsUnsigned) != 0);

        // Check if they passed us a string with no parsable digits.
        if (i == grabNumbersStart)
            throw new FormatException("No parsible digits");

        if ((flags & IsTight) != 0)
        {
            // If we've got effluvia left at the end of the string, complain.
            if (i < length)
                throw new FormatException("Extra junk at end");
        }

        // Put the current index back into the correct place.
        currPos = i;

        // Return the value properly signed.
        if ((flags & TreatAsI1) != 0)
        {
            if ((uint)result > 0xFF)
                throw new OverflowException();
        }
        else if ((flags & TreatAsI2) != 0)
        {
            if ((uint)result > 0xFFFF)
                throw new OverflowException();
        }
        else if ((uint)result == 0x80000000 && sign == 1 && r == 10 && ((flags & TreatAsUnsigned) == 0))
        {
            throw new OverflowException();
        }

        if (r == 10)
        {
            result *= sign;
        }

        return result;
    }

    private static void EatWhiteSpace(ReadOnlySpan<char> s, ref int i)
    {
        int localIndex = i;
        for (; localIndex < s.Length && char.IsWhiteSpace(s[localIndex]); localIndex++) ;
        i = localIndex;
    }

    private static long GrabLongs(int radix, ReadOnlySpan<char> s, ref int i, bool isUnsigned)
    {
        ulong result = 0;
        ulong maxVal;

        // Allow all non-decimal numbers to set the sign bit.
        if (radix == 10 && !isUnsigned)
        {
            maxVal = 0x7FFFFFFFFFFFFFFF / 10;

            // Read all of the digits and convert to a number
            while (i < s.Length && IsDigit(s[i], radix, out int value))
            {
                // Check for overflows - this is sufficient & correct.
                if (result > maxVal || ((long)result) < 0)
                {
                    throw new OverflowException();
                }

                result = result * (ulong)radix + (ulong)value;
                i++;
            }

            if ((long)result < 0 && result != 0x8000000000000000)
            {
                throw new OverflowException();
            }
        }
        else
        {
            Debug.Assert(radix == 2 || radix == 8 || radix == 10 || radix == 16);
            maxVal =
                radix == 10 ? 0xffffffffffffffff / 10 :
                radix == 16 ? 0xffffffffffffffff / 16 :
                radix == 8 ? 0xffffffffffffffff / 8 :
                0xffffffffffffffff / 2;

            // Read all of the digits and convert to a number
            while (i < s.Length && IsDigit(s[i], radix, out int value))
            {
                // Check for overflows - this is sufficient & correct.
                if (result > maxVal)
                {
                    throw new OverflowException();
                }

                ulong temp = result * (ulong)radix + (ulong)value;

                if (temp < result) // this means overflow as well
                {
                    throw new OverflowException();
                }

                result = temp;
                i++;
            }
        }

        return (long)result;
    }

    private static int GrabInts(int radix, ReadOnlySpan<char> s, ref int i, bool isUnsigned)
    {
        uint result = 0;
        uint maxVal;

        // Allow all non-decimal numbers to set the sign bit.
        if (radix == 10 && !isUnsigned)
        {
            maxVal = (0x7FFFFFFF / 10);

            // Read all of the digits and convert to a number
            while (i < s.Length && IsDigit(s[i], radix, out int value))
            {
                // Check for overflows - this is sufficient & correct.
                if (result > maxVal || (int)result < 0)
                {
                    throw new OverflowException();
                }
                result = result * (uint)radix + (uint)value;
                i++;
            }
            if ((int)result < 0 && result != 0x80000000)
            {
                throw new OverflowException();
            }
        }
        else
        {
            Debug.Assert(radix == 2 || radix == 8 || radix == 10 || radix == 16);
            maxVal =
                radix == 10 ? 0xffffffff / 10 :
                radix == 16 ? 0xffffffff / 16 :
                radix == 8 ? 0xffffffff / 8 :
                0xffffffff / 2;

            // Read all of the digits and convert to a number
            while (i < s.Length && IsDigit(s[i], radix, out int value))
            {
                // Check for overflows - this is sufficient & correct.
                if (result > maxVal)
                {
                    throw new OverflowException();
                }

                uint temp = result * (uint)radix + (uint)value;

                if (temp < result) // this means overflow as well
                {
                    throw new OverflowException();
                }

                result = temp;
                i++;
            }
        }

        return (int)result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDigit(char c, int radix, out int result)
    {
        int tmp;
        if ((uint)(c - '0') <= 9)
        {
            result = tmp = c - '0';
        }
        else if ((uint)(c - 'A') <= 'Z' - 'A')
        {
            result = tmp = c - 'A' + 10;
        }
        else if ((uint)(c - 'a') <= 'z' - 'a')
        {
            result = tmp = c - 'a' + 10;
        }
        else
        {
            result = -1;
            return false;
        }

        return tmp < radix;
    }
}
#endif