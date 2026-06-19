using System.Security.Cryptography;

namespace PostHog.Library;

internal static class UuidV7
{
    static readonly object Sync = new();
    static readonly RandomNumberGenerator Random = RandomNumberGenerator.Create();
    static readonly char[] Hex = "0123456789abcdef".ToCharArray();
    static readonly byte[] LastRandom = new byte[10];

    static long LastTimestamp = -1;

    public static string NewString()
    {
        var bytes = new byte[16];

        lock (Sync)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (timestamp <= LastTimestamp)
            {
                timestamp = LastTimestamp;
                IncrementRandom();
            }
            else
            {
                LastTimestamp = timestamp;
                Random.GetBytes(LastRandom);
            }

            bytes[0] = (byte)(timestamp >> 40);
            bytes[1] = (byte)(timestamp >> 32);
            bytes[2] = (byte)(timestamp >> 24);
            bytes[3] = (byte)(timestamp >> 16);
            bytes[4] = (byte)(timestamp >> 8);
            bytes[5] = (byte)timestamp;

            Buffer.BlockCopy(LastRandom, 0, bytes, 6, LastRandom.Length);
        }

        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return Format(bytes);
    }

    static void IncrementRandom()
    {
        for (var i = LastRandom.Length - 1; i >= 0; i--)
        {
            LastRandom[i]++;
            if (LastRandom[i] != 0)
            {
                return;
            }
        }
    }

    static string Format(byte[] bytes)
    {
        var chars = new char[36];
        var charIndex = 0;

        for (var i = 0; i < bytes.Length; i++)
        {
            if (i is 4 or 6 or 8 or 10)
            {
                chars[charIndex++] = '-';
            }

            var b = bytes[i];
            chars[charIndex++] = Hex[b >> 4];
            chars[charIndex++] = Hex[b & 0x0F];
        }

        return new string(chars);
    }
}
