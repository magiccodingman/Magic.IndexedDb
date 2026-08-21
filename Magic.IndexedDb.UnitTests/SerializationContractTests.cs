using System.Text.Json;
using System.Text.Json.Serialization;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class SerializationContractTests
{
    [TestMethod]
    public void EscapedStrings_RoundTripWithoutCorruption()
    {
        var expected = new EscapedValue
        {
            Text = "slash\\ newline\n tab\t quote\" control\u0001 snowman ☃"
        };

        var json = MagicSerializationHelper.SerializeObject(expected);
        var actual = MagicSerializationHelper.DeserializeObject<EscapedValue>(json);

        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.Text, actual.Text);
        Assert.IsTrue(JsonDocument.Parse(json).RootElement.TryGetProperty(nameof(EscapedValue.Text), out _));
    }

    [TestMethod]
    public void NestedCollections_RoundTripWithTheirShapeIntact()
    {
        var expected = new NestedCollections
        {
            Values = [[1, 2], [3, 4, 5]]
        };

        var json = MagicSerializationHelper.SerializeObject(expected);
        var actual = MagicSerializationHelper.DeserializeObject<NestedCollections>(json);

        Assert.IsNotNull(actual);
        CollectionAssert.AreEqual(new[] { 1, 2 }, actual.Values[0]);
        CollectionAssert.AreEqual(new[] { 3, 4, 5 }, actual.Values[1]);
    }

    [TestMethod]
    public void NestedComplexCollections_PreserveMagicPropertyNames()
    {
        var expected = new NestedComplexCollections
        {
            Values = [[new RenamedValue { Value = "mapped" }]]
        };

        var json = MagicSerializationHelper.SerializeObject(expected);
        var actual = MagicSerializationHelper.DeserializeObject<NestedComplexCollections>(json);

        StringAssert.Contains(json, "renamed_value");
        Assert.IsNotNull(actual);
        Assert.AreEqual("mapped", actual.Values[0][0].Value);
    }

    [TestMethod]
    public void HashSets_RoundTripAsHashSets()
    {
        var expected = new CollectionShapes { Tags = ["stable", "unique"] };

        var json = MagicSerializationHelper.SerializeObject(expected);
        var actual = MagicSerializationHelper.DeserializeObject<CollectionShapes>(json);

        Assert.IsNotNull(actual);
        Assert.IsInstanceOfType<HashSet<string>>(actual.Tags);
        Assert.IsTrue(actual.Tags.SetEquals(expected.Tags));
    }

    [TestMethod]
    public void Dictionaries_RoundTripKeysAndValues()
    {
        var expected = new Dictionary<string, object?>
        {
            ["count"] = 2,
            ["enabled"] = false,
            ["label"] = "value"
        };

        var json = MagicSerializationHelper.SerializeObject(expected);
        var actual = MagicSerializationHelper.DeserializeObject<Dictionary<string, object?>>(json);

        Assert.IsNotNull(actual);
        Assert.AreEqual(3, actual.Count);
        Assert.AreEqual("value", ((JsonElement)actual["label"]!).GetString());
        Assert.IsFalse(((JsonElement)actual["enabled"]!).GetBoolean());
    }

    [TestMethod]
    public void DictionaryProperties_RoundTripInsideEntityCollections()
    {
        DictionaryContainer[] expected =
        [
            new()
            {
                Id = 7,
                Metadata = new Dictionary<string, object?>
                {
                    ["count"] = 2,
                    ["enabled"] = false,
                    ["label"] = "value",
                    ["missing"] = null
                }
            }
        ];

        var json = MagicSerializationHelper.SerializeObject<IEnumerable<DictionaryContainer>>(expected);
        var actual = MagicSerializationHelper
            .DeserializeObject<IEnumerable<DictionaryContainer>>(json)?
            .Single();

        Assert.IsNotNull(actual);
        Assert.AreEqual(7, actual.Id);
        Assert.AreEqual(4, actual.Metadata.Count);
        Assert.AreEqual(2, ((JsonElement)actual.Metadata["count"]!).GetInt32());
        Assert.IsFalse(((JsonElement)actual.Metadata["enabled"]!).GetBoolean());
        Assert.AreEqual("value", ((JsonElement)actual.Metadata["label"]!).GetString());
        Assert.IsNull(actual.Metadata["missing"]);
    }

    [TestMethod]
    public void Dictionaries_RoundTripInsideNestedCollections()
    {
        var expected = new DictionaryCollections
        {
            Values =
            [
                new Dictionary<string, int> { ["one"] = 1 },
                new Dictionary<string, int> { ["two"] = 2 }
            ]
        };

        var json = MagicSerializationHelper.SerializeObject(expected);
        var actual = MagicSerializationHelper.DeserializeObject<DictionaryCollections>(json);

        Assert.IsNotNull(actual);
        Assert.AreEqual(1, actual.Values[0]["one"]);
        Assert.AreEqual(2, actual.Values[1]["two"]);
    }

    [TestMethod]
    public void ReadOnlyDictionaryProperties_RoundTripAsJsonObjects()
    {
        var expected = new ReadOnlyDictionaryContainer
        {
            Values = new Dictionary<string, int> { ["answer"] = 42 }
        };

        var json = MagicSerializationHelper.SerializeObject(expected);
        var actual = MagicSerializationHelper.DeserializeObject<ReadOnlyDictionaryContainer>(json);

        Assert.IsNotNull(actual);
        Assert.AreEqual(42, actual.Values["answer"]);
    }

    [TestMethod]
    public void ConfiguredEnumConverter_IsHonoredInBothDirections()
    {
        var settings = new MagicJsonSerializationSettings();
        settings.Options.Converters.Add(new JsonStringEnumConverter());
        var expected = new EnumValue { Status = LargeStatus.BeyondInt32 };

        var json = MagicSerializationHelper.SerializeObject(expected, settings);
        var actual = MagicSerializationHelper.DeserializeObject<EnumValue>(json, settings);

        StringAssert.Contains(json, nameof(LargeStatus.BeyondInt32));
        Assert.IsNotNull(actual);
        Assert.AreEqual(LargeStatus.BeyondInt32, actual.Status);
    }

    [TestMethod]
    public void ConfiguredSimpleTypeConverter_IsHonoredInBothDirections()
    {
        var settings = new MagicJsonSerializationSettings();
        settings.Options.Converters.Add(new FixedDateConverter());
        var expected = new DateValue { Date = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc) };

        var json = MagicSerializationHelper.SerializeObject(expected, settings);
        var actual = MagicSerializationHelper.DeserializeObject<DateValue>(json, settings);

        StringAssert.Contains(json, "fixed-date");
        Assert.IsNotNull(actual);
        Assert.AreEqual(FixedDateConverter.Value, actual.Date);
    }

    [TestMethod]
    public void BrowserRelevantScalarTypes_RoundTripWithoutPrecisionOrIdentityLoss()
    {
        var expected = new ScalarValues
        {
            Identifier = Guid.NewGuid(),
            Signed = long.MinValue + 17,
            Unsigned = ulong.MaxValue - 17,
            Money = 7922816251426433759354395.0335m,
            Moment = new DateTimeOffset(2040, 2, 29, 12, 34, 56, TimeSpan.FromHours(-5))
        };

        var actual = MagicSerializationHelper.DeserializeObject<ScalarValues>(
            MagicSerializationHelper.SerializeObject(expected));

        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.Identifier, actual.Identifier);
        Assert.AreEqual(expected.Signed, actual.Signed);
        Assert.AreEqual(expected.Unsigned, actual.Unsigned);
        Assert.AreEqual(expected.Money, actual.Money);
        Assert.AreEqual(expected.Moment, actual.Moment);
    }

    [TestMethod]
    public void NullAndEmptyCollectionShapes_RemainDistinct()
    {
        var expected = new NullableShapes
        {
            Missing = null,
            Empty = [],
            Values = [null, "", "value"]
        };

        var actual = MagicSerializationHelper.DeserializeObject<NullableShapes>(
            MagicSerializationHelper.SerializeObject(expected));

        Assert.IsNotNull(actual);
        Assert.IsNull(actual.Missing);
        Assert.IsNotNull(actual.Empty);
        Assert.AreEqual(0, actual.Empty.Count);
        CollectionAssert.AreEqual(expected.Values, actual.Values);
    }

    private sealed class EscapedValue
    {
        public string Text { get; set; } = string.Empty;
    }

    private sealed class NestedCollections
    {
        public List<List<int>> Values { get; set; } = [];
    }

    private sealed class CollectionShapes
    {
        public HashSet<string> Tags { get; set; } = [];
    }

    private sealed class NestedComplexCollections
    {
        public List<List<RenamedValue>> Values { get; set; } = [];
    }

    private sealed class RenamedValue
    {
        [Magic.IndexedDb.SchemaAnnotations.MagicName("renamed_value")]
        public string Value { get; set; } = string.Empty;
    }

    private sealed class EnumValue
    {
        public LargeStatus Status { get; set; }
    }

    private sealed class DictionaryContainer
    {
        public int Id { get; set; }
        public Dictionary<string, object?> Metadata { get; set; } = [];
    }

    private sealed class DictionaryCollections
    {
        public List<Dictionary<string, int>> Values { get; set; } = [];
    }

    private sealed class ReadOnlyDictionaryContainer
    {
        public IReadOnlyDictionary<string, int> Values { get; set; } =
            new Dictionary<string, int>();
    }

    private sealed class DateValue
    {
        public DateTime Date { get; set; }
    }

    private sealed class ScalarValues
    {
        public Guid Identifier { get; set; }
        public long Signed { get; set; }
        public ulong Unsigned { get; set; }
        public decimal Money { get; set; }
        public DateTimeOffset Moment { get; set; }
    }

    private sealed class NullableShapes
    {
        public List<string>? Missing { get; set; }
        public List<string> Empty { get; set; } = [];
        public List<string?> Values { get; set; } = [];
    }

    private sealed class FixedDateConverter : JsonConverter<DateTime>
    {
        public static DateTime Value { get; } = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Assert.AreEqual("fixed-date", reader.GetString());
            return Value;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue("fixed-date");
        }
    }

    private enum LargeStatus : ulong
    {
        BeyondInt32 = (ulong)int.MaxValue + 10UL
    }
}
