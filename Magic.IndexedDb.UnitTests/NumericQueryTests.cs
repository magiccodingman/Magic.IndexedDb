using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.LinqTranslation.Extensions;
using Magic.IndexedDb.LinqTranslation.Models;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;
using Microsoft.JSInterop;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class NumericQueryTests
{
    private const long BeyondJavaScriptInteger = 9_007_199_254_740_993;
    private const long MaximumNativeKey = 9_007_199_254_740_992;
    private static MagicJsonSerializationSettings Lossless() => new()
    {
        UseCamelCase = true,
        NumericStorageMode = MagicNumericStorageMode.Lossless
    };

    [TestMethod]
    public void NumericOperandsUseTheSameEncodingAsTheirStoredProperties()
    {
        var wide = BeyondJavaScriptInteger;
        var unsigned = ulong.MaxValue;
        var exact = 0.0000000000000000000000000001m;
        var record = new NumericRecord { Id = wide, Unsigned = unsigned, Amount = exact };
        var stored = MagicSerializationHelper.SerializeObjectToJsonElement(record, Lossless());
        Assert.AreEqual(stored.GetProperty(nameof(NumericRecord.Id)).GetString(),
            Operand<NumericRecord>(row => row.Id == wide).GetString());
        Assert.AreEqual(stored.GetProperty(nameof(NumericRecord.Unsigned)).GetString(),
            Operand<NumericRecord>(row => row.Unsigned > unsigned).GetString());
        Assert.AreEqual(stored.GetProperty(nameof(NumericRecord.Amount)).GetString(),
            Operand<NumericRecord>(row => row.Amount < exact).GetString());
        Assert.AreEqual(MagicNumericCodec.Encode(1m), Operand<NumericRecord>(row => row.Amount >= 1).GetString());
    }

    [TestMethod]
    public void WideEnumOperandsRetainSignedAndUnsignedUnderlyingRepresentations()
    {
        Assert.AreEqual(MagicNumericCodec.Encode(long.MaxValue),
            Operand<NumericRecord>(row => row.SignedStatus == SignedStatus.High).GetString());
        Assert.AreEqual(MagicNumericCodec.Encode(ulong.MaxValue),
            Operand<NumericRecord>(row => row.UnsignedStatus == UnsignedStatus.High).GetString());
        Assert.AreEqual(MagicNumericCodec.Encode(long.MinValue),
            Operand<NumericRecord>(row => (long)row.SignedStatus >= long.MinValue).GetString());
    }

    [TestMethod]
    public void NullableMembershipPreservesNullAndExactWideAlternatives()
    {
        long?[] values = [null, BeyondJavaScriptInteger];
        var node = new UniversalExpressionBuilder<NumericRecord>(row => values.Contains(row.Optional), Lossless()).Build();
        var json = MagicSerializationHelper.SerializeObjectToJsonElement(node, Lossless());
        var operands = json.GetProperty("children").EnumerateArray()
            .Select(child => child.GetProperty("condition").GetProperty("value")).ToArray();
        Assert.AreEqual(JsonValueKind.Null, operands[0].ValueKind);
        Assert.AreEqual(MagicNumericCodec.Encode(BeyondJavaScriptInteger), operands[1].GetString());
    }

    [TestMethod]
    public void LegacyBuilderAndSerializationKeepExistingNumericJson()
    {
        var value = BeyondJavaScriptInteger;
        var node = new UniversalExpressionBuilder<NumericRecord>(row => row.Id == value).Build();
        var json = MagicSerializationHelper.SerializeObjectToJsonElement(node,
            new MagicJsonSerializationSettings { UseCamelCase = true });
        var operand = json.GetProperty("condition").GetProperty("value");
        Assert.AreEqual(JsonValueKind.Number, operand.ValueKind);
        Assert.AreEqual(value, operand.GetInt64());
    }

    [TestMethod]
    public void NativeAutoIncrementKeyQueriesKeepExactNumberAtTheGeneratorBoundary()
    {
        var operand = Operand<AutoNumericRecord>(row => row.Id >= MaximumNativeKey);
        Assert.AreEqual(JsonValueKind.Number, operand.ValueKind);
        Assert.AreEqual(MaximumNativeKey, operand.GetInt64());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Operand<AutoNumericRecord>(row => row.Id == BeyondJavaScriptInteger));
    }

    [TestMethod]
    public void NumericMemberConversionsThatWouldChangeComparisonSemanticsFailExplicitly()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            Operand<NumericRecord>(row => (decimal)row.Id > 1.5m));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            Operand<NumericRecord>(row => (int)row.Amount == 1));
    }

    [TestMethod]
    public void ApplicationPropertyConverterKeepsOwnershipOfItsWireRepresentation()
    {
        var value = BeyondJavaScriptInteger;
        var stored = MagicSerializationHelper.SerializeObjectToJsonElement(new NumericRecord { Custom = value }, Lossless());
        Assert.AreEqual($"custom:{value}", stored.GetProperty(nameof(NumericRecord.Custom)).GetString());
        Assert.AreEqual(stored.GetProperty(nameof(NumericRecord.Custom)).GetString(),
            Operand<NumericRecord>(row => row.Custom == value).GetString());
        Assert.ThrowsExactly<InvalidOperationException>(() => Operand<NumericRecord>(row => row.Custom > value));
    }

    [TestMethod]
    public void MismatchedObjectEqualityCannotMatchAnEncodedNumericString()
    {
        object wide = 1L;
        object integer = 1;
        var record = new NumericRecord { Id = 1, Text = MagicNumericCodec.Encode(1L) };
        Expression<Func<NumericRecord, bool>> textEqualsNumber = row => row.Text.Equals(wide);
        Assert.IsFalse(textEqualsNumber.Compile()(record));
        Assert.ThrowsExactly<InvalidOperationException>(() => Operand(textEqualsNumber));
        Assert.ThrowsExactly<InvalidOperationException>(() => Operand<NumericRecord>(row => !row.Text.Equals(wide)));
        Assert.ThrowsExactly<InvalidOperationException>(() => Operand<NumericRecord>(row => row.Id.Equals(integer)));
        Assert.ThrowsExactly<InvalidOperationException>(() => Operand<NumericRecord>(row => row.SignedStatus.Equals(wide)));

        // The opt-in guard does not change the Legacy translator contract.
        var legacy = new UniversalExpressionBuilder<NumericRecord>(textEqualsNumber).Build();
        var json = MagicSerializationHelper.SerializeObjectToJsonElement(legacy,
            new MagicJsonSerializationSettings { UseCamelCase = true });
        Assert.AreEqual(JsonValueKind.Number, json.GetProperty("condition").GetProperty("value").ValueKind);
    }

    [TestMethod]
    public void MatchingBoxedTypesAndLiteralCodecStringsRemainQueryable()
    {
        string text = MagicNumericCodec.Encode(1L);
        object boxedText = text;
        object boxedLong = BeyondJavaScriptInteger;
        object boxedEnum = SignedStatus.High;
        Assert.AreEqual(text, Operand<NumericRecord>(row => row.Text == text).GetString());
        Assert.AreEqual(text, Operand<NumericRecord>(row => row.Text.Equals(boxedText)).GetString());
        Assert.AreEqual(MagicNumericCodec.Encode(BeyondJavaScriptInteger),
            Operand<NumericRecord>(row => row.Id.Equals(boxedLong)).GetString());
        Assert.AreEqual(MagicNumericCodec.Encode(BeyondJavaScriptInteger),
            Operand<NumericRecord>(row => row.Optional.Equals(boxedLong)).GetString());
        Assert.AreEqual(MagicNumericCodec.Encode(long.MaxValue),
            Operand<NumericRecord>(row => row.SignedStatus.Equals(boxedEnum)).GetString());
        Assert.AreEqual(MagicNumericCodec.Encode(1L), Operand<NumericRecord>(row => row.Id.Equals(1)).GetString());
    }

    [TestMethod]
    public async Task ManagerUsesEachDatabasesPolicyWithoutLeakingItToOtherDatabases()
    {
        var browser = new RecordingBrowser();
        var manager = Manager(browser);
        var record = new NumericRecord { Id = BeyondJavaScriptInteger, Amount = decimal.MaxValue };
        await manager.AddAsync<NumericRecord, JsonElement>(record, "lossless");
        await manager.AddAsync<NumericRecord, JsonElement>(record, "legacy");
        var encoded = browser.Packages[0].GetProperty("parameters")[0].GetProperty("record");
        var legacy = browser.Packages[1].GetProperty("parameters")[0].GetProperty("record");
        Assert.AreEqual(MagicNumericCodec.Encode(record.Id), encoded.GetProperty(nameof(NumericRecord.Id)).GetString());
        Assert.AreEqual(JsonValueKind.Number, legacy.GetProperty(nameof(NumericRecord.Id)).ValueKind);
        Assert.AreEqual(record.Id, legacy.GetProperty(nameof(NumericRecord.Id)).GetInt64());
    }

    [TestMethod]
    public async Task UpdateAndDeleteKeyMetadataMatchesEncodedRecordKeys()
    {
        var browser = new RecordingBrowser { Reply = "1" };
        var manager = Manager(browser);
        var record = new NumericRecord { Id = BeyondJavaScriptInteger };
        await manager.UpdateAsync(record, "lossless");
        var update = browser.Packages[^1].GetProperty("parameters")[0];
        Assert.AreEqual(update.GetProperty("record").GetProperty(nameof(NumericRecord.Id)).GetString(),
            update.GetProperty("key")[0].GetProperty("value").GetString());

        browser.Reply = "true";
        await manager.DeleteAsync(record, "lossless");
        var delete = browser.Packages[^1].GetProperty("parameters")[0];
        Assert.AreEqual(MagicNumericCodec.Encode(record.Id), delete.GetProperty("key")[0].GetProperty("value").GetString());

        browser.Reply = "2";
        await manager.DeleteRangeAsync(new[] { record, new NumericRecord { Id = long.MaxValue } }, "lossless");
        var keys = browser.Packages[^1].GetProperty("parameters")[2];
        Assert.AreEqual(MagicNumericCodec.Encode(long.MaxValue), keys[1][0].GetProperty("value").GetString());
    }

    [TestMethod]
    public async Task NativeAutoIncrementRecordAndKeyMetadataUseTheSameExactNumber()
    {
        var browser = new RecordingBrowser { Reply = "1" };
        var manager = Manager(browser);
        await manager.UpdateAsync(new AutoNumericRecord { Id = MaximumNativeKey, Amount = 1.1m }, "lossless");
        var update = browser.Packages[^1].GetProperty("parameters")[0];
        Assert.AreEqual(MaximumNativeKey, update.GetProperty("record").GetProperty(nameof(AutoNumericRecord.Id)).GetInt64());
        Assert.AreEqual(MaximumNativeKey, update.GetProperty("key")[0].GetProperty("value").GetInt64());
        Assert.AreEqual(MagicNumericCodec.Encode(1.1m), update.GetProperty("record").GetProperty(nameof(AutoNumericRecord.Amount)).GetString());

        browser.Reply = MaximumNativeKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreEqual(MaximumNativeKey,
            await manager.AddAsync<AutoNumericRecord, long>(new AutoNumericRecord { Amount = 1.1m }, "lossless"));
    }

    [TestMethod]
    public async Task TypedReadAndPublicProjectionDecodeLosslessValues()
    {
        var browser = new RecordingBrowser
        {
            Reply = JsonSerializer.Serialize(new[] { new { Id = MagicNumericCodec.Encode(BeyondJavaScriptInteger), Amount = MagicNumericCodec.Encode(decimal.MaxValue) } })
        };
        var manager = Manager(browser);
        var query = manager.Query<NumericRecord>("lossless", nameof(NumericRecord));
        var rows = await query.ToListAsync();
        Assert.AreEqual(BeyondJavaScriptInteger, rows.Single().Id);
        Assert.AreEqual(decimal.MaxValue, rows.Single().Amount);
        var amounts = await query.SelectPublicAsync(row => row.Amount);
        Assert.AreEqual(decimal.MaxValue, amounts.Single());
    }

    [TestMethod]
    public async Task PublicPatchEncodesBothItsKeyAndValueUsingTheActiveDatabase()
    {
        var browser = new RecordingBrowser { Reply = "1" };
        var query = Manager(browser).Query<NumericRecord>("lossless", nameof(NumericRecord));
        await query.PatchPublicAsync(new NumericRecord { Id = BeyondJavaScriptInteger, Amount = decimal.MinValue }, row => row.Amount);
        var args = browser.Packages.Single().GetProperty("parameters");
        Assert.AreEqual(MagicNumericCodec.Encode(BeyondJavaScriptInteger), args[2].GetString());
        Assert.AreEqual(MagicNumericCodec.Encode(decimal.MinValue), args[3].GetProperty(nameof(NumericRecord.Amount)).GetString());
    }

    [TestMethod]
    public async Task PublicProjectionRetainsMappedComplexValuesAndPropertyConverters()
    {
        var browser = new RecordingBrowser { Reply = "[{\"Child\":{\"mapped\":\"kept\"},\"Custom\":\"custom:9007199254740993\"}]" };
        var query = Manager(browser).Query<NumericRecord>("lossless", nameof(NumericRecord));
        Assert.AreEqual("kept", (await query.SelectPublicAsync(row => row.Child)).Single()!.Value);
        Assert.AreEqual(BeyondJavaScriptInteger, (await query.SelectPublicAsync(row => row.Custom)).Single());
    }

    private static JsonElement Operand<T>(Expression<Func<T, bool>> predicate)
    {
        var node = new UniversalExpressionBuilder<T>(predicate, Lossless()).Build();
        var json = MagicSerializationHelper.SerializeObjectToJsonElement(node, Lossless());
        return json.GetProperty("condition").GetProperty("value");
    }

    private static IndexedDbManager Manager(RecordingBrowser browser) => new(browser, 1024,
        numericModes: new Dictionary<string, MagicNumericStorageMode> { ["lossless"] = MagicNumericStorageMode.Lossless });

    private sealed class RecordingBrowser : IJSObjectReference
    {
        public List<JsonElement> Packages { get; } = [];
        public string Reply { get; set; } = "true";
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args)
        {
            Assert.AreEqual("streamedJsHandler", identifier);
            var stream = ((DotNetStreamReference)args![0]!).Stream;
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            Packages.Add(document.RootElement.Clone());
            return (TValue)(object)new Response(Reply);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Response(string json) : IJSStreamReference
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(json);
        public long Length => _bytes.Length;
        public ValueTask<Stream> OpenReadStreamAsync(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(_bytes));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed class Repository : IMagicRepository
    {
        public static readonly IndexedDbSet Database = new("numbers");
    }

    public sealed class NumericRecord : MagicTableTool<NumericRecord>, IMagicTable<Repository>
    {
        public long Id { get; set; }
        public string Text { get; set; } = "";
        public ulong Unsigned { get; set; }
        public decimal Amount { get; set; }
        public long? Optional { get; set; }
        public SignedStatus SignedStatus { get; set; }
        public UnsignedStatus UnsignedStatus { get; set; }
        [JsonConverter(typeof(PrefixedLongConverter))]
        public long Custom { get; set; }
        public Child Child { get; set; } = new();
        public Repository Databases { get; } = new();
        public IndexedDbSet GetDefaultDatabase() => Repository.Database;
        public string GetTableName() => nameof(NumericRecord);
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, autoIncrement: false);
        public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
    }

    public sealed class AutoNumericRecord : MagicTableTool<AutoNumericRecord>, IMagicTable<Repository>
    {
        public long Id { get; set; }
        public decimal Amount { get; set; }
        public Repository Databases { get; } = new();
        public IndexedDbSet GetDefaultDatabase() => Repository.Database;
        public string GetTableName() => nameof(AutoNumericRecord);
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, autoIncrement: true);
        public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
    }

    public sealed class Child
    {
        [MagicName("mapped")]
        public string Value { get; set; } = "";
    }
    public enum SignedStatus : long { Low = long.MinValue, High = long.MaxValue }
    public enum UnsignedStatus : ulong { High = ulong.MaxValue }
    public sealed class PrefixedLongConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            long.Parse(reader.GetString()![7..], System.Globalization.CultureInfo.InvariantCulture);
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
            writer.WriteStringValue("custom:" + value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
