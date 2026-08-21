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

    private sealed class DateValue
    {
        public DateTime Date { get; set; }
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
