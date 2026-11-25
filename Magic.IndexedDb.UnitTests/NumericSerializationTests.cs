using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class NumericSerializationTests
{
    private static MagicJsonSerializationSettings Lossless() => new() { NumericStorageMode = MagicNumericStorageMode.Lossless };

    [TestMethod]
    public void WideScalarsBecomeStringsBeforeJavaScriptCanParseThem()
    {
        var settings = Lossless();
        Assert.AreEqual(MagicNumericCodec.Encode(long.MaxValue), MagicSerializationHelper.SerializeObjectToJsonElement(long.MaxValue, settings).GetString());
        Assert.AreEqual(MagicNumericCodec.Encode(ulong.MaxValue), MagicSerializationHelper.SerializeObjectToJsonElement(ulong.MaxValue, settings).GetString());
        const decimal amount = 7922816251426433759354395.0335m;
        Assert.AreEqual(MagicNumericCodec.Encode(amount), MagicSerializationHelper.SerializeObjectToJsonElement(amount, settings).GetString());
        Assert.AreEqual(amount, MagicSerializationHelper.DeserializeObject<decimal>(MagicSerializationHelper.SerializeObject(amount, settings), settings));
        Assert.AreEqual(123, MagicSerializationHelper.SerializeObjectToJsonElement(123, settings).GetInt32());
        Assert.AreEqual(1.25, MagicSerializationHelper.SerializeObjectToJsonElement(1.25, settings).GetDouble());
    }

    [TestMethod]
    public void NestedCollectionsNullableValuesAndWideEnumsRoundTripExactly()
    {
        var expected = new Values
        {
            Signed = long.MinValue, Unsigned = ulong.MaxValue, Amount = decimal.MaxValue,
            Nullable = long.MaxValue, State = WideState.High, OptionalState = WideState.High,
            ValuesByName = new() { ["amount"] = -0.0000000000000000000000000001m },
            ValuesById = new() { [ulong.MaxValue] = long.MinValue },
            List = [null, long.MinValue, long.MaxValue], Child = new() { Amount = 9007199254740993m }
        };
        var settings = Lossless();
        var json = MagicSerializationHelper.SerializeObject(expected, settings);
        var actual = MagicSerializationHelper.DeserializeObject<Values>(json, settings)!;
        Assert.AreEqual(expected.Signed, actual.Signed);
        Assert.AreEqual(expected.Unsigned, actual.Unsigned);
        Assert.AreEqual(expected.Amount, actual.Amount);
        Assert.AreEqual(expected.Nullable, actual.Nullable);
        Assert.AreEqual(expected.State, actual.State);
        Assert.AreEqual(expected.OptionalState, actual.OptionalState);
        Assert.AreEqual(expected.ValuesByName["amount"], actual.ValuesByName["amount"]);
        Assert.AreEqual(long.MinValue, actual.ValuesById[ulong.MaxValue]);
        CollectionAssert.AreEqual(expected.List, actual.List);
        Assert.AreEqual(expected.Child.Amount, actual.Child.Amount);
        Assert.IsNull(MagicSerializationHelper.DeserializeObject<long?>("null", settings));
        Assert.AreEqual(WideState.High, MagicSerializationHelper.DeserializeObject<WideState?>(
            MagicSerializationHelper.SerializeObject<WideState?>(WideState.High, settings), settings));
    }

    [TestMethod]
    public void LegacyRemainsDefaultAndLosslessRejectsUnencodedWideNumbers()
    {
        Assert.AreEqual("9223372036854775807", MagicSerializationHelper.SerializeObject(long.MaxValue));
        Assert.AreEqual("18446744073709551615", MagicSerializationHelper.SerializeObject(ulong.MaxValue));
        Assert.AreEqual("123.456", MagicSerializationHelper.SerializeObject(123.456m));
        Assert.Throws<JsonException>(() => MagicSerializationHelper.DeserializeObject<long>("12", Lossless()));
        Assert.Throws<JsonException>(() => MagicSerializationHelper.DeserializeObject<decimal>("0.1", Lossless()));
        Assert.Throws<ArgumentOutOfRangeException>(() => MagicSerializationHelper.SerializeObject(1L,
            new() { NumericStorageMode = (MagicNumericStorageMode)99 }));
    }

    [TestMethod]
    public void PropertyAndStringEnumConvertersRetainTheirDeclaredRepresentation()
    {
        var expected = new CustomValues { Amount = 123.4500m, State = WideState.High };
        var settings = Lossless();
        var element = MagicSerializationHelper.SerializeObjectToJsonElement(expected, settings);
        Assert.AreEqual("custom:123.4500", element.GetProperty(nameof(CustomValues.Amount)).GetString());
        Assert.AreEqual("High", element.GetProperty(nameof(CustomValues.State)).GetString());
        var actual = MagicSerializationHelper.DeserializeObject<CustomValues>(element.GetRawText(), settings)!;
        Assert.AreEqual(expected.Amount, actual.Amount);
        Assert.AreEqual(expected.State, actual.State);
    }

    [TestMethod]
    public void AutoIncrementKeysStayNativeWhileOtherWideValuesAreEncoded()
    {
        var settings = Lossless();
        var value = new AutoRecord { Id = 9_007_199_254_740_992L, Amount = decimal.MaxValue };
        var json = MagicSerializationHelper.SerializeObjectToJsonElement(value, settings);
        Assert.AreEqual(value.Id, json.GetProperty(nameof(AutoRecord.Id)).GetInt64());
        Assert.AreEqual(MagicNumericCodec.Encode(value.Amount), json.GetProperty(nameof(AutoRecord.Amount)).GetString());
        var decoded = MagicSerializationHelper.DeserializeObject<AutoRecord>(json.GetRawText(), settings)!;
        Assert.AreEqual(value.Id, decoded.Id);
        Assert.AreEqual(value.Amount, decoded.Amount);
        Assert.IsFalse(MagicSerializationHelper.SerializeObjectToJsonElement(new AutoRecord(), settings)
            .TryGetProperty(nameof(AutoRecord.Id), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => MagicSerializationHelper.SerializeObject(
            new AutoRecord { Id = 9_007_199_254_740_993L }, settings));
        Assert.Throws<ArgumentOutOfRangeException>(() => MagicNumericSerialization.SerializeNativeKey(1.5m, typeof(decimal)));
        Assert.Throws<ArgumentOutOfRangeException>(() => MagicNumericSerialization.SerializeNativeKey(WideState.High, typeof(WideState)));
    }

    public enum WideState : ulong { Low = 0, High = ulong.MaxValue }
    public sealed class Values
    {
        public long Signed { get; set; }
        public ulong Unsigned { get; set; }
        public decimal Amount { get; set; }
        public long? Nullable { get; set; }
        public WideState State { get; set; }
        public WideState? OptionalState { get; set; }
        public Dictionary<string, decimal> ValuesByName { get; set; } = [];
        public Dictionary<ulong, long> ValuesById { get; set; } = [];
        public long?[] List { get; set; } = [];
        public ChildValues Child { get; set; } = new();
    }
    public sealed class ChildValues { public decimal Amount { get; set; } }
    public sealed class CustomValues
    {
        [JsonConverter(typeof(AmountConverter))]
        public decimal Amount { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter<WideState>))]
        public WideState State { get; set; }
    }
    public sealed class AmountConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            decimal.Parse(reader.GetString()!["custom:".Length..], CultureInfo.InvariantCulture);
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteStringValue("custom:" + value.ToString(CultureInfo.InvariantCulture));
    }
    public sealed class AutoRecord : MagicTableTool<AutoRecord>, IMagicTable<Repository>
    {
        public long Id { get; set; }
        public decimal Amount { get; set; }
        public Repository Databases { get; } = new();
        public IndexedDbSet GetDefaultDatabase() => Repository.Database;
        public string GetTableName() => nameof(AutoRecord);
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, autoIncrement: true);
        public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
    }
    public sealed class Repository : IMagicRepository { public static readonly IndexedDbSet Database = new("Numeric.Unit"); }
}
