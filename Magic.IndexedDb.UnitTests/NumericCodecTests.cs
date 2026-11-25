using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class NumericCodecTests
{
    [TestMethod]
    public void FormatVersionOne_HasStableBoundaryEncodings()
    {
        Assert.AreEqual(1, MagicNumericCodec.PolicyVersion);
        Assert.AreEqual("~ml1:00000000000000000000", MagicNumericCodec.Encode(long.MinValue));
        Assert.AreEqual("~ml1:09223372036854775808", MagicNumericCodec.Encode(0L));
        Assert.AreEqual("~ml1:18446744073709551615", MagicNumericCodec.Encode(long.MaxValue));
        Assert.AreEqual("~mu1:00000000000000000000", MagicNumericCodec.Encode(0UL));
        Assert.AreEqual("~mu1:18446744073709551615", MagicNumericCodec.Encode(ulong.MaxValue));
        Assert.AreEqual("~md1:0000000000000000000000000000000000000000000000000000000000",
            MagicNumericCodec.Encode(decimal.MinValue));
        Assert.AreEqual("~md1:0792281625142643375935439503350000000000000000000000000000",
            MagicNumericCodec.Encode(0m));
        Assert.AreEqual("~md1:0792281625142643375935439503360000000000000000000000000000",
            MagicNumericCodec.Encode(1m));
        Assert.AreEqual("~md1:1584563250285286751870879006700000000000000000000000000000",
            MagicNumericCodec.Encode(decimal.MaxValue));
    }

    [TestMethod]
    public void SignedAndUnsignedIntegers_RoundTripAcrossSafeIntegerAndTypeBoundaries()
    {
        long[] signed = [
            long.MinValue, long.MinValue + 1,
            -9007199254740993L, -9007199254740992L, -9007199254740991L,
            -1L, 0L, 1L, 9007199254740991L, 9007199254740992L, 9007199254740993L,
            long.MaxValue - 1, long.MaxValue
        ];
        ulong[] unsigned = [
            0UL, 1UL, 9007199254740991UL, 9007199254740992UL, 9007199254740993UL,
            9223372036854775807UL, 9223372036854775808UL, 9223372036854775809UL,
            ulong.MaxValue - 1, ulong.MaxValue
        ];

        foreach (long value in signed)
            Assert.AreEqual(value, MagicNumericCodec.DecodeInt64(MagicNumericCodec.Encode(value)));
        foreach (ulong value in unsigned)
            Assert.AreEqual(value, MagicNumericCodec.DecodeUInt64(MagicNumericCodec.Encode(value)));

        AssertOrdinalOrder(signed, MagicNumericCodec.Encode);
        AssertOrdinalOrder(unsigned, MagicNumericCodec.Encode);
    }

    [TestMethod]
    public void Decimals_RoundTripExtremesAndEveryLegalScale()
    {
        var values = new List<decimal>
        {
            decimal.MinValue, decimal.MinValue + 1m,
            -9007199254740993m, -1m, 0m, 1m, 9007199254740993m,
            0.0000000000000000000000000001m,
            -0.0000000000000000000000000001m,
            7922816251426433759354395.0335m,
            -7922816251426433759354395.0335m,
            decimal.MaxValue - 1m, decimal.MaxValue
        };
        for (byte scale = 0; scale <= 28; scale++)
        {
            values.Add(new decimal(-1, -1, -1, false, scale));
            values.Add(new decimal(-1, -1, -1, true, scale));
            values.Add(new decimal(1, 0, 0, false, scale));
            values.Add(new decimal(1, 0, 0, true, scale));
        }

        foreach (decimal value in values)
        {
            string encoded = MagicNumericCodec.Encode(value);
            decimal decoded = MagicNumericCodec.DecodeDecimal(encoded);
            Assert.AreEqual(0, decimal.Compare(value, decoded));
            Assert.AreEqual(encoded, MagicNumericCodec.Encode(decoded));
        }
        AssertOrdinalOrder(values, MagicNumericCodec.Encode);
    }

    [TestMethod]
    public void DecimalEquality_NormalizesScaleAndSignedZero()
    {
        string one = MagicNumericCodec.Encode(1m);
        string minusOne = MagicNumericCodec.Encode(-1m);
        string zero = MagicNumericCodec.Encode(0m);

        for (byte scale = 0; scale <= 28; scale++)
        {
            string text = scale == 0 ? "1" : "1." + new string('0', scale);
            decimal value = decimal.Parse(text, CultureInfo.InvariantCulture);
            Assert.AreEqual(one, MagicNumericCodec.Encode(value));
            Assert.AreEqual(minusOne, MagicNumericCodec.Encode(-value));
            Assert.AreEqual(zero, MagicNumericCodec.Encode(new decimal(0, 0, 0, false, scale)));
            Assert.AreEqual(zero, MagicNumericCodec.Encode(new decimal(0, 0, 0, true, scale)));
        }

        CollectionAssert.AreEqual(new[] { 0, 0, 0, 0 }, decimal.GetBits(MagicNumericCodec.DecodeDecimal(zero)));
        CollectionAssert.AreEqual(decimal.GetBits(1m), decimal.GetBits(MagicNumericCodec.DecodeDecimal(one)));
        Assert.AreEqual(MagicNumericCodec.Encode(123.45m), MagicNumericCodec.Encode(123.450000m));
    }

    [TestMethod]
    public void SeededRandomIntegers_RoundTripAndSortLikeTheirNumericTypes()
    {
        var random = new Random(143);
        var bytes = new byte[8];
        var signed = new long[4096];
        var unsigned = new ulong[4096];
        for (int index = 0; index < signed.Length; index++)
        {
            random.NextBytes(bytes);
            signed[index] = BinaryPrimitives.ReadInt64LittleEndian(bytes);
            unsigned[index] = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            Assert.AreEqual(signed[index], MagicNumericCodec.DecodeInt64(MagicNumericCodec.Encode(signed[index])));
            Assert.AreEqual(unsigned[index], MagicNumericCodec.DecodeUInt64(MagicNumericCodec.Encode(unsigned[index])));
        }

        AssertOrdinalOrder(signed, MagicNumericCodec.Encode);
        AssertOrdinalOrder(unsigned, MagicNumericCodec.Encode);
    }

    [TestMethod]
    public void SeededRandomDecimals_RoundTripAndSortLikeDecimalCompare()
    {
        var random = new Random(144);
        var bytes = new byte[12];
        var values = new decimal[4096];
        for (int index = 0; index < values.Length; index++)
        {
            random.NextBytes(bytes);
            values[index] = new decimal(
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4)),
                random.Next(2) != 0, (byte)random.Next(29));
            string encoded = MagicNumericCodec.Encode(values[index]);
            Assert.AreEqual(0, decimal.Compare(values[index], MagicNumericCodec.DecodeDecimal(encoded)));
            Assert.AreEqual(encoded, MagicNumericCodec.Encode(MagicNumericCodec.DecodeDecimal(encoded)));
        }

        AssertOrdinalOrder(values, MagicNumericCodec.Encode);
        for (int index = 1; index < values.Length; index++)
        {
            Assert.AreEqual(Math.Sign(decimal.Compare(values[index - 1], values[index])),
                Math.Sign(StringComparer.Ordinal.Compare(
                    MagicNumericCodec.Encode(values[index - 1]), MagicNumericCodec.Encode(values[index]))));
        }
    }

    [TestMethod]
    public void Encodings_AreInvariantUnderNonEnglishCultures()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            string signed = MagicNumericCodec.Encode(long.MinValue + 1);
            string unsigned = MagicNumericCodec.Encode(ulong.MaxValue - 1);
            string fractional = MagicNumericCodec.Encode(-123456789.1234567890123456789m);
            foreach (string culture in new[] { "fr-FR", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Assert.AreEqual(signed, MagicNumericCodec.Encode(long.MinValue + 1));
                Assert.AreEqual(unsigned, MagicNumericCodec.Encode(ulong.MaxValue - 1));
                Assert.AreEqual(fractional, MagicNumericCodec.Encode(-123456789.1234567890123456789m));
                Assert.AreEqual(long.MinValue + 1, MagicNumericCodec.DecodeInt64(signed));
                Assert.AreEqual(ulong.MaxValue - 1, MagicNumericCodec.DecodeUInt64(unsigned));
                Assert.AreEqual(-123456789.1234567890123456789m, MagicNumericCodec.DecodeDecimal(fractional));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void Decoders_RejectWrongVersionsTypesLengthsAndNonAsciiDigits()
    {
        AssertInvalidForms(MagicNumericCodec.Encode(0L), "~ml1:", MagicNumericCodec.DecodeInt64);
        AssertInvalidForms(MagicNumericCodec.Encode(0UL), "~mu1:", MagicNumericCodec.DecodeUInt64);
        AssertInvalidForms(MagicNumericCodec.Encode(0m), "~md1:", MagicNumericCodec.DecodeDecimal);

        Assert.ThrowsExactly<FormatException>(() => MagicNumericCodec.DecodeInt64(MagicNumericCodec.Encode(0UL)));
        Assert.ThrowsExactly<FormatException>(() => MagicNumericCodec.DecodeUInt64(MagicNumericCodec.Encode(0L)));
        Assert.ThrowsExactly<FormatException>(() => MagicNumericCodec.DecodeDecimal(MagicNumericCodec.Encode(0L)));
        Assert.ThrowsExactly<ArgumentNullException>(() => MagicNumericCodec.DecodeInt64(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => MagicNumericCodec.DecodeUInt64(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => MagicNumericCodec.DecodeDecimal(null!));
    }

    [TestMethod]
    public void Decoders_RejectValuesBeyondTheRepresentedTypeRange()
    {
        Assert.ThrowsExactly<FormatException>(() => MagicNumericCodec.DecodeInt64("~ml1:18446744073709551616"));
        Assert.ThrowsExactly<FormatException>(() => MagicNumericCodec.DecodeUInt64("~mu1:18446744073709551616"));
        Assert.ThrowsExactly<FormatException>(() => MagicNumericCodec.DecodeDecimal(
            "~md1:1584563250285286751870879006700000000000000000000000000001"));
    }

    [TestMethod]
    public void DecimalDecoder_RejectsInRangeValuesRequiringMoreThanNinetySixCoefficientBits()
    {
        // These scaled integers lie inside the overall interval, but neither is
        // divisible by ten and their magnitudes exceed Decimal's coefficient.
        var offset = BigInteger.Parse(
            "792281625142643375935439503350000000000000000000000000000",
            CultureInfo.InvariantCulture);
        var unrepresentable = BigInteger.One << 96;
        foreach (var biased in new[] { offset + unrepresentable, offset - unrepresentable })
        {
            string encoded = "~md1:" + biased.ToString("D58", CultureInfo.InvariantCulture);
            Assert.ThrowsExactly<FormatException>(() => MagicNumericCodec.DecodeDecimal(encoded));
        }
    }

    private static void AssertOrdinalOrder<T>(IEnumerable<T> values, Func<T, string> encode)
    {
        var expected = values.OrderBy(value => value, Comparer<T>.Default).ToArray();
        var actual = values.OrderBy(encode, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(expected, actual);
    }

    private static void AssertInvalidForms<T>(string valid, string prefix, Func<string, T> decode)
    {
        string[] invalid = [
            "", prefix, valid[1..], valid + "0", valid[..^1],
            valid.Replace("1:", "2:", StringComparison.Ordinal),
            valid.ToUpperInvariant(),
            prefix + "+" + valid[(prefix.Length + 1)..],
            prefix + "-" + valid[(prefix.Length + 1)..],
            prefix + " " + valid[(prefix.Length + 1)..],
            prefix + "\u0660" + valid[(prefix.Length + 1)..],
            prefix + "\0" + valid[(prefix.Length + 1)..],
            prefix + "." + valid[(prefix.Length + 1)..],
            prefix + "e" + valid[(prefix.Length + 1)..]
        ];
        foreach (string encoded in invalid)
            Assert.ThrowsExactly<FormatException>(() => decode(encoded));
    }
}
