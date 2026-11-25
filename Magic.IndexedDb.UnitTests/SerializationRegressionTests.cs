using System.Text.Json;
using System.Text.Json.Serialization;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;
using TestBase.Models;
using TestBase.Repository;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class SerializationRegressionTests
{
    [TestMethod]
    public void ExplicitPrimaryKeys_PreserveZeroIncludingCompoundKeyParts()
    {
        var single = MagicSerializationHelper.SerializeObjectToJsonElement(new ExplicitZeroKey());
        var compound = MagicSerializationHelper.SerializeObjectToJsonElement(new CompoundZeroKey { Tenant = "tenant" });

        Assert.AreEqual(0, single.GetProperty("id").GetInt32());
        Assert.AreEqual(0, compound.GetProperty(nameof(CompoundZeroKey.Sequence)).GetInt32());
        Assert.AreEqual("tenant", compound.GetProperty(nameof(CompoundZeroKey.Tenant)).GetString());
    }

    [TestMethod]
    public void GeneratedPrimaryKeys_OmitOnlyTheDefaultValue()
    {
        var generated = MagicSerializationHelper.SerializeObjectToJsonElement(new GeneratedKey());
        var supplied = MagicSerializationHelper.SerializeObjectToJsonElement(new GeneratedKey { Id = 17 });

        Assert.IsFalse(generated.TryGetProperty(nameof(GeneratedKey.Id), out _));
        Assert.AreEqual(17, supplied.GetProperty(nameof(GeneratedKey.Id)).GetInt32());
    }

    [TestMethod]
    public void JsonElementPropertiesAndCollections_PreserveTheirJsonValues()
    {
        using var document = JsonDocument.Parse("{\"message\":\"retained\",\"values\":[null,4,true]}");
        using var jsonNull = JsonDocument.Parse("null");
        var expected = new JsonElementRecord
        {
            Payload = document.RootElement.Clone(),
            Values = [document.RootElement.Clone(), jsonNull.RootElement.Clone()]
        };

        var json = MagicSerializationHelper.SerializeObject(expected);
        var actual = MagicSerializationHelper.DeserializeObject<JsonElementRecord>(json)!;

        Assert.AreEqual("retained", actual.Payload.GetProperty("message").GetString());
        Assert.AreEqual(4, actual.Payload.GetProperty("values")[1].GetInt32());
        Assert.AreEqual("retained", actual.Values[0].GetProperty("message").GetString());
        Assert.AreEqual(JsonValueKind.Null, actual.Values[1].ValueKind);
        Assert.AreEqual(JsonValueKind.Null,
            MagicSerializationHelper.DeserializeObject<JsonElement>("null").ValueKind);
    }

    [TestMethod]
    public void DateOnlyAndTimeOnly_UseSystemTextJsonScalarRepresentations()
    {
        var expected = new CalendarRecord
        {
            Date = new DateOnly(2032, 2, 29),
            Time = new TimeOnly(13, 45, 56, 789)
        };

        var json = MagicSerializationHelper.SerializeObject(expected);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual("2032-02-29", document.RootElement.GetProperty(nameof(CalendarRecord.Date)).GetString());
        var actual = MagicSerializationHelper.DeserializeObject<CalendarRecord>(json)!;
        Assert.AreEqual(expected.Date, actual.Date);
        Assert.AreEqual(expected.Time, actual.Time);
    }

    [TestMethod]
    public void PropertyConverter_IsUsedWhenWritingAndReading()
    {
        var json = MagicSerializationHelper.SerializeObject(new PropertyConvertedRecord { Value = 42 });
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual("number:42", document.RootElement.GetProperty(nameof(PropertyConvertedRecord.Value)).GetString());
        Assert.AreEqual(42, MagicSerializationHelper.DeserializeObject<PropertyConvertedRecord>(json)!.Value);
    }

    [TestMethod]
    public void NestedTypeConverter_IsUsedWhenWritingAndReading()
    {
        var json = MagicSerializationHelper.SerializeObject(new TypeConvertedRecord { Value = new EncodedValue(42) });
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual("number:42", document.RootElement.GetProperty(nameof(TypeConvertedRecord.Value)).GetString());
        Assert.AreEqual(42, MagicSerializationHelper.DeserializeObject<TypeConvertedRecord>(json)!.Value.Number);
    }

    [TestMethod]
    public void MutableStructs_RoundTripWithoutLosingPropertyAssignments()
    {
        var expected = new Coordinates { X = 17, Y = 23 };
        var actual = MagicSerializationHelper.DeserializeObject<Coordinates>(MagicSerializationHelper.SerializeObject(expected));
        Assert.AreEqual(expected.X, actual.X);
        Assert.AreEqual(expected.Y, actual.Y);
    }

    [TestMethod]
    public void NullableComplexStructs_PreserveValuesAndNull()
    {
        var expected = new NullableStructRecord { Point = new Coordinates { X = 17, Y = 23 } };
        var actual = MagicSerializationHelper.DeserializeObject<NullableStructRecord>(MagicSerializationHelper.SerializeObject(expected))!;
        Assert.IsTrue(actual.Point.HasValue);
        Assert.AreEqual(expected.Point.Value.X, actual.Point.Value.X);
        Assert.AreEqual(expected.Point.Value.Y, actual.Point.Value.Y);

        actual = MagicSerializationHelper.DeserializeObject<NullableStructRecord>("{\"Point\":null}")!;
        Assert.IsNull(actual.Point);
    }

    [TestMethod]
    public void NullableTypeConverters_ApplyToRootPropertiesAndCollectionElements()
    {
        EncodedStruct? expected = new EncodedStruct(42);
        var rootJson = MagicSerializationHelper.SerializeObject(expected);
        Assert.AreEqual("\"number:42\"", rootJson);
        Assert.AreEqual(expected, MagicSerializationHelper.DeserializeObject<EncodedStruct?>(rootJson));
        Assert.IsNull(MagicSerializationHelper.DeserializeObject<EncodedStruct?>("null"));

        var record = new NullableConvertedRecord { Value = expected, Values = [expected, null] };
        var actual = MagicSerializationHelper.DeserializeObject<NullableConvertedRecord>(
            MagicSerializationHelper.SerializeObject(record))!;
        Assert.AreEqual(expected, actual.Value);
        CollectionAssert.AreEqual(record.Values, actual.Values);
        actual = MagicSerializationHelper.DeserializeObject<NullableConvertedRecord>("{\"Value\":null,\"Values\":[]}")!;
        Assert.IsNull(actual.Value);
    }

    private sealed class JsonElementRecord
    {
        public JsonElement Payload { get; set; }
        public List<JsonElement> Values { get; set; } = [];
    }

    private sealed class CalendarRecord
    {
        public DateOnly Date { get; set; }
        public TimeOnly Time { get; set; }
    }

    private sealed class PropertyConvertedRecord
    {
        [JsonConverter(typeof(EncodedIntConverter))]
        public int Value { get; set; }
    }

    private sealed class TypeConvertedRecord
    {
        public EncodedValue Value { get; set; } = new(0);
    }

    [JsonConverter(typeof(EncodedValueConverter))]
    public sealed record EncodedValue(int Number);

    public sealed class EncodedIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            int.Parse(reader.GetString()!.AsSpan("number:".Length));
        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"number:{value}");
    }

    public sealed class EncodedValueConverter : JsonConverter<EncodedValue>
    {
        public override EncodedValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(int.Parse(reader.GetString()!.AsSpan("number:".Length)));
        public override void Write(Utf8JsonWriter writer, EncodedValue value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"number:{value.Number}");
    }

    [JsonConverter(typeof(EncodedStructConverter))]
    public readonly record struct EncodedStruct(int Number);

    public sealed class EncodedStructConverter : JsonConverter<EncodedStruct>
    {
        public override EncodedStruct Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(int.Parse(reader.GetString()!.AsSpan("number:".Length)));
        public override void Write(Utf8JsonWriter writer, EncodedStruct value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"number:{value.Number}");
    }

    private sealed class NullableConvertedRecord
    {
        public EncodedStruct? Value { get; set; }
        public List<EncodedStruct?> Values { get; set; } = [];
    }

    public struct Coordinates
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    private sealed class NullableStructRecord
    {
        public Coordinates? Point { get; set; }
    }

    private sealed class ExplicitZeroKey : MagicTableTool<ExplicitZeroKey>, IMagicTable<Person.DbSets>
    {
        [MagicName("id")]
        public int Id { get; set; }
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, autoIncrement: false);
        public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
        public string GetTableName() => nameof(ExplicitZeroKey);
        public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;
        public Person.DbSets Databases { get; } = new();
    }

    private sealed class CompoundZeroKey : MagicTableTool<CompoundZeroKey>, IMagicTable<Person.DbSets>
    {
        public string Tenant { get; set; } = "";
        public int Sequence { get; set; }
        public IMagicCompoundKey GetKeys() => CreateCompoundKey(row => row.Tenant, row => row.Sequence);
        public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
        public string GetTableName() => nameof(CompoundZeroKey);
        public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;
        public Person.DbSets Databases { get; } = new();
    }

    private sealed class GeneratedKey : MagicTableTool<GeneratedKey>, IMagicTable<Person.DbSets>
    {
        public int Id { get; set; }
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, autoIncrement: true);
        public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
        public string GetTableName() => nameof(GeneratedKey);
        public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;
        public Person.DbSets Databases { get; } = new();
    }
}
