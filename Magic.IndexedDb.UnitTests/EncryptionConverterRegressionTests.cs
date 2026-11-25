using System.Text.Json;
using System.Text.Json.Serialization;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.SchemaAnnotations;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class EncryptionConverterRegressionTests
{
    [TestMethod]
    public void FieldEncryption_RejectsAWholeTableConverterThatMovesTheProtectedValue()
    {
        var row = new ConvertedRecord { Id = 7, Secret = "synthetic private value" };
        var serialized = MagicSerializationHelper.SerializeObjectToJsonElement(row);
        Assert.AreEqual(7, serialized.GetProperty("id").GetInt32());
        Assert.AreEqual(row.Secret, serialized.GetProperty("payload").GetString());
        Assert.IsFalse(serialized.TryGetProperty(nameof(ConvertedRecord.Secret), out _));

        var configuration = new EncryptionConfiguration(new("records", "account", "key"));
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            configuration.CreateDatabase([(typeof(ConvertedRecord), Schema<ConvertedRecord>())]));
        StringAssert.Contains(error.Message, "whole-record encryption");
    }

    [TestMethod]
    public void ConfiguredRecordEncryption_AllowsConvertedPayloadOutsideThePublicAllowlist()
    {
        var configuration = new EncryptionConfiguration(new("records", "account", "key",
            new Dictionary<string, IReadOnlyCollection<string>>
            {
                [nameof(ConvertedRecord)] = ["id"]
            }));

        var database = configuration.CreateDatabase([(typeof(ConvertedRecord), Schema<ConvertedRecord>())]);
        var schema = database.GetProperty("storeSchemas")[0];
        CollectionAssert.AreEqual(new[] { "id" },
            schema.GetProperty("exposedFields").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.IsFalse(schema.GetProperty("exposedFields").EnumerateArray().Any(value => value.GetString() == "payload"));
    }

    [TestMethod]
    public void AnnotatedRecordEncryption_AlsoAllowsTheWholeTableConverter()
    {
        var configuration = new EncryptionConfiguration(new("records", "account", "key"));
        var database = configuration.CreateDatabase([(typeof(AnnotatedConvertedRecord), Schema<AnnotatedConvertedRecord>())]);

        CollectionAssert.AreEqual(new[] { "id" }, database.GetProperty("storeSchemas")[0]
            .GetProperty("exposedFields").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    [TestMethod]
    public void PropertyConverter_RemainsSupportedInsideAnEncryptedField()
    {
        var row = new PropertyConvertedRecord { Id = 7, Secret = "synthetic private value" };
        var serialized = MagicSerializationHelper.SerializeObjectToJsonElement(row);
        Assert.AreEqual("encoded:" + row.Secret, serialized.GetProperty(nameof(PropertyConvertedRecord.Secret)).GetString());

        var configuration = new EncryptionConfiguration(new("records", "account", "key"));
        var database = configuration.CreateDatabase([(typeof(PropertyConvertedRecord), Schema<PropertyConvertedRecord>())]);
        var schema = database.GetProperty("storeSchemas")[0];
        Assert.AreEqual(JsonValueKind.Null, schema.GetProperty("exposedFields").ValueKind);
        CollectionAssert.AreEqual(new[] { nameof(PropertyConvertedRecord.Secret) },
            schema.GetProperty("encryptedFields").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    private static StoreSchema Schema<T>() => new()
    {
        TableName = typeof(T).Name,
        ColumnNamesInCompoundKey = ["id"]
    };

    [JsonConverter(typeof(RenamingConverter<ConvertedRecord>))]
    public class ConvertedRecord
    {
        [MagicName("id")]
        public int Id { get; set; }

        [MagicEncrypt]
        public string Secret { get; set; } = "";
    }

    [MagicEncryptRecord(nameof(Id))]
    [JsonConverter(typeof(RenamingConverter<AnnotatedConvertedRecord>))]
    public sealed class AnnotatedConvertedRecord : ConvertedRecord;

    public sealed class RenamingConverter<T> : JsonConverter<T> where T : ConvertedRecord, new()
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return new T
            {
                Id = document.RootElement.GetProperty("id").GetInt32(),
                Secret = document.RootElement.GetProperty("payload").GetString()!
            };
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", value.Id);
            writer.WriteString("payload", value.Secret);
            writer.WriteEndObject();
        }
    }

    public sealed class PropertyConvertedRecord
    {
        [MagicName("id")]
        public int Id { get; set; }

        [MagicEncrypt, JsonConverter(typeof(SecretConverter))]
        public string Secret { get; set; } = "";
    }

    public sealed class SecretConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString()!["encoded:".Length..];

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue("encoded:" + value);
    }
}
