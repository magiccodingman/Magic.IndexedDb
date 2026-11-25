using System.Globalization;
using System.Numerics;

namespace Magic.IndexedDb;

/// <summary>
/// Exact, versioned numeric strings whose ordinal order is numeric order within
/// each supported type. IndexedDB compares string keys by their code units.
/// </summary>
internal static class MagicNumericCodec
{
    internal const int PolicyVersion = 1;
    internal const string Int64Prefix = "~ml1:";
    internal const string UInt64Prefix = "~mu1:";
    internal const string DecimalPrefix = "~md1:";

    private const int IntegerDigits = 20;
    private const int DecimalDigits = 58;
    private const byte DecimalScale = 28;
    private const ulong SignBit = 1UL << 63;
    private static readonly BigInteger[] PowersOfTen = CreatePowersOfTen();
    private static readonly BigInteger MaxDecimalCoefficient = (BigInteger.One << 96) - BigInteger.One;
    private static readonly BigInteger DecimalOffset = MaxDecimalCoefficient * PowersOfTen[DecimalScale];
    private static readonly BigInteger MaxEncodedDecimal = DecimalOffset * 2;

    internal static string Encode(long value)
    {
        // Flipping the sign bit is the exact unsigned representation of x + 2^63.
        ulong biased = unchecked((ulong)value) ^ SignBit;
        return Int64Prefix + biased.ToString("D20", CultureInfo.InvariantCulture);
    }

    internal static string Encode(ulong value) =>
        UInt64Prefix + value.ToString("D20", CultureInfo.InvariantCulture);

    internal static string Encode(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        int scale = (bits[3] >> 16) & 0xff;
        var coefficient = (BigInteger)(uint)bits[0] |
                          ((BigInteger)(uint)bits[1] << 32) |
                          ((BigInteger)(uint)bits[2] << 64);
        var scaled = coefficient * PowersOfTen[DecimalScale - scale];
        if (bits[3] < 0) scaled = -scaled;

        // Decimal is an integer multiple of 10^-28. Scaling and adding the same
        // offset is monotone, and the fixed-width digits preserve that ordering.
        // Equal values with different scales, including signed zero, map identically.
        return DecimalPrefix + (scaled + DecimalOffset).ToString("D58", CultureInfo.InvariantCulture);
    }

    internal static long DecodeInt64(string encoded)
    {
        ulong biased = ParseInteger(encoded, Int64Prefix);
        return unchecked((long)(biased ^ SignBit));
    }

    internal static ulong DecodeUInt64(string encoded) => ParseInteger(encoded, UInt64Prefix);

    internal static decimal DecodeDecimal(string encoded)
    {
        var digits = RequireDigits(encoded, DecimalPrefix, DecimalDigits);
        if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var biased) ||
            biased > MaxEncodedDecimal)
        {
            throw InvalidEncoding();
        }

        var scaled = biased - DecimalOffset;
        if (scaled.IsZero) return decimal.Zero;

        bool negative = scaled.Sign < 0;
        var coefficient = BigInteger.Abs(scaled);
        byte scale = DecimalScale;
        while (scale > 0)
        {
            var quotient = BigInteger.DivRem(coefficient, 10, out var remainder);
            if (!remainder.IsZero) break;
            coefficient = quotient;
            scale--;
        }

        // The fixed-width interval also contains numbers that Decimal cannot
        // represent. Reject those gaps instead of rounding to a different value.
        if (coefficient > MaxDecimalCoefficient) throw InvalidEncoding();

        int low = unchecked((int)(uint)(coefficient & uint.MaxValue));
        int middle = unchecked((int)(uint)((coefficient >> 32) & uint.MaxValue));
        int high = unchecked((int)(uint)((coefficient >> 64) & uint.MaxValue));
        return new decimal(low, middle, high, negative, scale);
    }

    private static ulong ParseInteger(string encoded, string prefix)
    {
        var digits = RequireDigits(encoded, prefix, IntegerDigits);
        if (!ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value))
            throw InvalidEncoding();
        return value;
    }

    private static ReadOnlySpan<char> RequireDigits(string encoded, string prefix, int length)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (encoded.Length != prefix.Length + length || !encoded.StartsWith(prefix, StringComparison.Ordinal))
            throw InvalidEncoding();

        var digits = encoded.AsSpan(prefix.Length);
        foreach (char digit in digits)
        {
            if (digit is < '0' or > '9') throw InvalidEncoding();
        }
        return digits;
    }

    private static FormatException InvalidEncoding() =>
        new("Invalid or unsupported Magic numeric encoding.");

    private static BigInteger[] CreatePowersOfTen()
    {
        var powers = new BigInteger[DecimalScale + 1];
        powers[0] = BigInteger.One;
        for (int index = 1; index < powers.Length; index++) powers[index] = powers[index - 1] * 10;
        return powers;
    }
}
