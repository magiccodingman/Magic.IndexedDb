using System.Text.Json;
using System.Text.Json.Serialization;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Migrations;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class MigrationSchemaTests
{
    [TestMethod]
    public void AutomaticMembership_UsesDeclaredDefaultInsteadOfDeployingEverywhere()
    {
        var result = MagicSchemaResolver.ResolveDefinitions([typeof(ClientRow), typeof(StaffRow)],
            [new("clients"), new("staff")]);
        CollectionAssert.AreEqual(new[] { "clients" }, result[0].Schema.Stores.Select(store => store.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "staff" }, result[1].Schema.Stores.Select(store => store.Name).ToArray());
    }

    [TestMethod]
    public void HybridAndExplicitMembership_AreResolvedAndDeduplicated()
    {
        var definitions = new[]
        {
            new IndexedDbSet("clients").Include<StaffRow>().Include<StaffRow>(),
            new IndexedDbSet("archive").ExplicitTablesOnly().Include<ClientRow>()
        };
        var result = MagicSchemaResolver.ResolveDefinitions([typeof(ClientRow)], definitions);
        CollectionAssert.AreEqual(new[] { "clients" }, result[0].Schema.Stores.Select(store => store.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "clients", "staff" }, result[1].Schema.Stores.Select(store => store.Name).ToArray());
    }

    [TestMethod]
    public void ExplicitReuse_DoesNotRequireTheModelsDefaultDatabase()
    {
        var result = MagicSchemaResolver.ResolveDefinitions([], [new IndexedDbSet("archive")
            .ExplicitTablesOnly().Include<ClientRow>()]);
        Assert.AreEqual(typeof(ClientRow), result.Single().TableTypes.Single());
        Assert.AreEqual("archive", result.Single().Schema.SchemaId);
    }

    [TestMethod]
    public void Exclusion_PreservesDeclarationImmutability()
    {
        var original = new IndexedDbSet("clients");
        var excluded = original.Exclude<ClientRow>();
        Assert.AreEqual(0, original.ExcludedTypes.Count);
        Assert.AreEqual(1, excluded.ExcludedTypes.Count);
        Assert.AreEqual(0, MagicSchemaResolver.ResolveDefinitions([typeof(ClientRow)], [excluded]).Single().Schema.Stores.Count);
        Assert.AreEqual(1, MagicSchemaResolver.ResolveDefinitions([typeof(ClientRow)], [original]).Single().Schema.Stores.Count);
        var error = Assert.ThrowsExactly<MagicSchemaException>(() => excluded.Include<ClientRow>());
        Assert.AreEqual("MAGDB3008", error.Code);
    }

    [TestMethod]
    public void IdentityValidation_RejectsConflictingDeclarationsAndUnsafePaths()
    {
        var duplicate = Assert.ThrowsExactly<MagicSchemaException>(() => MagicSchemaResolver.ResolveDefinitions([], [
            new IndexedDbSet("first").WithSchemaId("shared"), new IndexedDbSet("second").WithSchemaId("shared")]));
        Assert.AreEqual("MAGDB3004", duplicate.Code);
        foreach (var id in new[] { "", "../escape", "schema/name", "schema\\name", "a\n", "雪" })
            Assert.ThrowsExactly<MagicSchemaException>(() => new IndexedDbSet("physical name").WithSchemaId(id));
        Assert.AreEqual("portable-id", new IndexedDbSet("physical name").WithSchemaId("portable-id").SchemaId);
    }

    [TestMethod]
    public void DuplicateStoreNames_FailBeforePublishing()
    {
        var error = Assert.ThrowsExactly<MagicSchemaException>(() => MagicSchemaResolver.ResolveDefinitions([], [
            new IndexedDbSet("clients").ExplicitTablesOnly().Include<ClientRow>().Include<ConflictingRow>()]));
        Assert.AreEqual("MAGDB3005", error.Code);
    }

    [TestMethod]
    public void Export_UsesActualSerializedNamesAndRemovesIgnoredFields()
    {
        var resolved = MagicSchemaResolver.ResolveDefinitions([typeof(ClientRow)], [new("clients")]).Single();
        var schema = resolved.Schema.Stores.Single();
        Assert.AreEqual("id", schema.KeyPath);
        var serialized = MagicSerializationHelper.SerializeObjectToJsonElement(new ClientRow(),
            new MagicJsonSerializationSettings { UseCamelCase = true });
        CollectionAssert.AreEquivalent(serialized.EnumerateObject().Select(property => property.Name).ToArray(),
            schema.Fields.Select(field => field.Name).ToArray());
        Assert.IsFalse(schema.Fields.Any(field => field.Name == nameof(ClientRow.Ignored)));
        Assert.AreEqual("firstName", schema.Fields.Single(field => field.Name == "firstName").Name);
        var contact = schema.Fields.Single(field => field.Name == "Contact");
        Assert.AreEqual("phone", contact.Properties!.Single().Name);
    }

    [TestMethod]
    public void NumericFormat_ChangesThePersistedContractWithoutChangingClrTypes()
    {
        var legacy = MagicSchemaResolver.ResolveDefinitions([typeof(ClientRow)], [new("clients")]).Single().Schema;
        var lossless = MagicSchemaResolver.ResolveDefinitions([typeof(ClientRow)], [new("clients")],
            new() { NumericModes = new Dictionary<string, MagicNumericStorageMode> { ["clients"] = MagicNumericStorageMode.Lossless } }).Single().Schema;
        Assert.AreEqual("lossless-v1", lossless.NumericFormat);
        Assert.AreEqual("numeric:lossless-v1", lossless.Stores.Single().Fields.Single(field => field.Name == "Balance").Codec);
        Assert.AreNotEqual(MagicSchemaJson.Fingerprint(legacy), MagicSchemaJson.Fingerprint(lossless));
        Assert.AreEqual("json:string-enum", lossless.Stores.Single().Fields.Single(field => field.Name == "Status").Codec);
    }

    [TestMethod]
    public void UnversionedCustomConverters_FailWithActionableDiagnostic()
    {
        var error = Assert.ThrowsExactly<MagicSchemaException>(() => MagicSchemaResolver.ResolveDefinitions(
            [typeof(CustomRow)], [new("custom")]));
        Assert.AreEqual("MAGDB3012", error.Code);
        StringAssert.Contains(error.Message, "MagicStorageCodec");
    }

    [TestMethod]
    public void ExplicitCustomCodec_IsPortableAndIncludedInFingerprint()
    {
        var schema = MagicSchemaResolver.ResolveDefinitions([typeof(VersionedCustomRow)], [new("custom")]).Single().Schema;
        Assert.AreEqual("epoch-seconds:2", schema.Stores.Single().Fields.Single(field => field.Name == "When").Codec);
        var json = MagicSchemaJson.Serialize(schema);
        Assert.IsFalse(json.Contains(typeof(EpochConverter).FullName!, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("System.Private.CoreLib", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ProtectedFields_AreDescribedAndCannotBeNativeIndexes()
    {
        var options = new MagicSchemaOptions { ProtectedDatabases = ["protected"] };
        var schema = MagicSchemaResolver.ResolveDefinitions([typeof(ProtectedRow)], [new("protected")], options).Single().Schema;
        CollectionAssert.AreEqual(new[] { "Secret" }, schema.Stores.Single().EncryptedFields!.ToArray());
        var error = Assert.ThrowsExactly<MagicSchemaException>(() => MagicSchemaResolver.ResolveDefinitions(
            [typeof(InvalidProtectedRow)], [new("protected")], options));
        Assert.AreEqual("MAGDB3011", error.Code);
    }

    [TestMethod]
    public void AuthenticatedHistory_RemainsExplicitWhenEveryFieldIsPublic()
    {
        var definition = new IndexedDbSet("clients");
        var ordinary = MagicSchemaResolver.ResolveDefinitions([typeof(ClientRow)], [definition]).Single().Schema;
        var authenticated = MagicSchemaResolver.ResolveDefinitions([typeof(ClientRow)], [definition],
            new() { ProtectedDatabases = ["clients"] }).Single().Schema;
        CollectionAssert.AreEqual(new[] { "authenticatedMigrationsV1" }, authenticated.RequiredCapabilities.ToArray());
        Assert.AreEqual(0, ordinary.RequiredCapabilities.Count);
        Assert.AreNotEqual(MagicSchemaJson.Fingerprint(ordinary), MagicSchemaJson.Fingerprint(authenticated));
        Assert.IsTrue(authenticated.Stores.All(store => store.EncryptedFields is null && store.ExposedFields is null));
    }

    [TestMethod]
    public void OrdinaryDatabase_SkipsProtectedDefaultsAndRejectsExplicitProtectedTables()
    {
        var schema = MagicSchemaResolver.ResolveDefinitions([typeof(ProtectedRow)], [new("protected")]).Single().Schema;
        Assert.AreEqual(0, schema.Stores.Count);
        var error = Assert.ThrowsExactly<MagicSchemaException>(() => MagicSchemaResolver.ResolveDefinitions([], [
            new IndexedDbSet("protected").ExplicitTablesOnly().Include<ProtectedRow>()]));
        Assert.AreEqual("MAGDB3011", error.Code);
    }

    [TestMethod]
    public void DictionaryValues_UseTheActualSystemTextJsonContract()
    {
        var schema = MagicSchemaResolver.ResolveDefinitions([typeof(DictionaryRow)], [new("dictionary")]).Single().Schema;
        var field = schema.Stores.Single().Fields.Single(field => field.Name == "Values");
        var serialized = MagicSerializationHelper.SerializeObjectToJsonElement(new DictionaryRow(),
            new MagicJsonSerializationSettings { UseCamelCase = true });
        var value = serialized.GetProperty("Values").GetProperty("sample");
        CollectionAssert.AreEquivalent(value.EnumerateObject().Select(property => property.Name).ToArray(),
            field.Items!.Properties!.Select(property => property.Name).ToArray());
        Assert.AreEqual("wireName", field.Items.Properties!.Single(property => property.Name == "wireName").Name);
        Assert.AreEqual("json:base64", field.Items.Properties!.Single(property => property.Name == "bytes").Codec);
        Assert.AreEqual("string", field.DictionaryKeys!.Type);
        Assert.IsFalse(field.Items.Nullable);
    }

    [TestMethod]
    public void StringEnumTokens_AreIncludedInThePortableContract()
    {
        var schema = MagicSchemaResolver.ResolveDefinitions([typeof(ClientRow)], [new("clients")]).Single().Schema;
        var field = schema.Stores.Single().Fields.Single(field => field.Name == "Status");
        Assert.AreEqual("active-client", field.EnumNames![nameof(ClientStatus.Active)]);
        Assert.AreEqual("1", field.EnumValues![nameof(ClientStatus.Active)]);
        Assert.AreEqual("integer32", field.EnumUnderlyingType);
        Assert.AreEqual(false, field.EnumFlags);
    }

    [TestMethod]
    public void UnmodeledSystemTextJsonFeatures_FailBeforeExportingAnIncompleteContract()
    {
        Type[] types = [typeof(DictionaryContractRow<ExtensionDataValue>),
            typeof(DictionaryContractRow<NumberStringValue>), typeof(DictionaryContractRow<PolymorphicValue>)];
        foreach (var type in types)
        {
            var error = Assert.ThrowsExactly<MagicSchemaException>(() =>
                MagicSchemaResolver.ResolveDefinitions([type], [new("dictionary")]));
            Assert.AreEqual("MAGDB3012", error.Code);
        }
    }

    [TestMethod]
    public void Export_IsDeterministicAcrossDiscoveryOrderAndContainsOnlyProtocolModels()
    {
        var first = MagicSchemaExporter.Export([typeof(ClientRow), typeof(StaffRow)], [typeof(Repository)]);
        var second = MagicSchemaExporter.Export([typeof(StaffRow), typeof(ClientRow)], [typeof(Repository)]);
        Assert.AreEqual(first, second);
        using var document = JsonDocument.Parse(first);
        Assert.AreEqual(1, document.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.AreEqual(2, document.RootElement.GetProperty("schemas").GetArrayLength());
        Assert.IsFalse(first.Contains(nameof(Repository), StringComparison.Ordinal));
    }

    [TestMethod]
    public void CanonicalJson_MatchesJavaScriptOrderingUnicodeAndNumberFormatting()
    {
        using var input = JsonDocument.Parse("{\"z\":1.0,\"é\":\"雪<>&+'\\n\\u2028\",\"a\":[0.000001,1e-7,1e20,1e21,-0]}");
        Assert.AreEqual("{\"a\":[0.000001,1e-7,100000000000000000000,1e+21,0],\"z\":1,\"é\":\"雪<>&+'\\n\u2028\"}",
            MagicSchemaJson.Canonicalize(input.RootElement));
        using var duplicates = JsonDocument.Parse("{\"same\":1,\"same\":2}");
        Assert.ThrowsExactly<MagicSchemaException>(() => MagicSchemaJson.Canonicalize(duplicates.RootElement));
    }

    [TestMethod]
    public void CanonicalJson_MatchesTheSharedJavaScriptProtocolVectors()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tests", "migrations", "canonical-vectors.json")))
            directory = directory.Parent;
        Assert.IsNotNull(directory, "The shared protocol corpus must be present in the repository checkout.");
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "tests", "migrations", "canonical-vectors.json")));
        foreach (var vector in vectors.RootElement.EnumerateArray())
        {
            using var input = JsonDocument.Parse(vector.GetProperty("json").GetString()!);
            Assert.AreEqual(vector.GetProperty("canonical").GetString(), MagicSchemaJson.Canonicalize(input.RootElement),
                vector.GetProperty("name").GetString());
        }
    }

    public sealed class Repository : IMagicRepository
    {
        public static readonly IndexedDbSet Clients = new("clients");
        public static readonly IndexedDbSet Staff = new("staff");
    }

    public class ClientRow : MagicTableTool<ClientRow>, IMagicTable<object>
    {
        [MagicName("id")] public int Id { get; set; }
        [MagicName("firstName")] public string Name { get; set; } = "Example";
        public decimal Balance { get; set; }
        public ContactInfo Contact { get; set; } = new();
        public ClientStatus Status { get; set; }
        [MagicNotMapped] public string Ignored { get; set; } = "ignored";
        [MagicNotMapped] public object Databases { get; } = new();
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
        public List<IMagicCompoundIndex>? GetCompoundIndexes() => [];
        public string GetTableName() => "clients";
        public IndexedDbSet GetDefaultDatabase() => Repository.Clients;
    }
    public sealed class ContactInfo { public string Phone { get; set; } = "123"; }
    [JsonConverter(typeof(JsonStringEnumConverter<ClientStatus>))]
    public enum ClientStatus { [JsonStringEnumMemberName("active-client")] Active = 1, Closed = 2 }

    public sealed class DictionaryRow : MagicTableTool<DictionaryRow>, IMagicTable<object>
    {
        public int Id { get; set; }
        public Dictionary<string, DictionaryValue> Values { get; set; } = new() { ["sample"] = new() };
        [MagicNotMapped] public object Databases { get; } = new();
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
        public List<IMagicCompoundIndex>? GetCompoundIndexes() => [];
        public string GetTableName() => "dictionary";
        public IndexedDbSet GetDefaultDatabase() => new("dictionary");
    }

    public sealed class DictionaryValue
    {
        [MagicName("magicName"), JsonPropertyName("wireName")] public string Name { get; set; } = "wire";
        [JsonIgnore] public string Ignored { get; set; } = "ignored";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public string InputOnly { get; set; } = "ignored";
        [MagicNotMapped] public string PresentInDictionary { get; set; } = "persisted";
        public byte[] Bytes { get; set; } = [1, 2, 3];
    }

    public sealed class DictionaryContractRow<T> : MagicTableTool<DictionaryContractRow<T>>, IMagicTable<object>
    {
        public int Id { get; set; }
        public Dictionary<string, T> Values { get; set; } = [];
        [MagicNotMapped] public object Databases { get; } = new();
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
        public List<IMagicCompoundIndex>? GetCompoundIndexes() => [];
        public string GetTableName() => "dictionary";
        public IndexedDbSet GetDefaultDatabase() => new("dictionary");
    }
    public sealed class ExtensionDataValue
    {
        [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; } = [];
    }
    public sealed class NumberStringValue
    {
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public int Number { get; set; }
    }
    [JsonDerivedType(typeof(DerivedValue), "derived")]
    public class PolymorphicValue { public string Name { get; set; } = ""; }
    public sealed class DerivedValue : PolymorphicValue { public bool Flag { get; set; } }

    public sealed class StaffRow : MagicTableTool<StaffRow>, IMagicTable<object>
    {
        public int Id { get; set; }
        [MagicNotMapped] public object Databases { get; } = new();
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
        public List<IMagicCompoundIndex>? GetCompoundIndexes() => [];
        public string GetTableName() => "staff";
        public IndexedDbSet GetDefaultDatabase() => Repository.Staff;
    }

    public sealed class ConflictingRow : MagicTableTool<ConflictingRow>, IMagicTable<object>
    {
        public int Id { get; set; }
        [MagicNotMapped] public object Databases { get; } = new();
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
        public List<IMagicCompoundIndex>? GetCompoundIndexes() => [];
        public string GetTableName() => "clients";
        public IndexedDbSet GetDefaultDatabase() => Repository.Clients;
    }

    public sealed class CustomRow : MagicTableTool<CustomRow>, IMagicTable<object>
    {
        public int Id { get; set; }
        [JsonConverter(typeof(EpochConverter))] public DateTime When { get; set; }
        [MagicNotMapped] public object Databases { get; } = new();
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
        public List<IMagicCompoundIndex>? GetCompoundIndexes() => [];
        public string GetTableName() => "custom";
        public IndexedDbSet GetDefaultDatabase() => new("custom");
    }

    public sealed class VersionedCustomRow : MagicTableTool<VersionedCustomRow>, IMagicTable<object>
    {
        public int Id { get; set; }
        [JsonConverter(typeof(EpochConverter)), MagicStorageCodec("epoch-seconds", 2)] public DateTime When { get; set; }
        [MagicNotMapped] public object Databases { get; } = new();
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
        public List<IMagicCompoundIndex>? GetCompoundIndexes() => [];
        public string GetTableName() => "custom";
        public IndexedDbSet GetDefaultDatabase() => new("custom");
    }

    public sealed class EpochConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.UnixEpoch.AddSeconds(reader.GetInt64());
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteNumberValue((long)(value - DateTime.UnixEpoch).TotalSeconds);
    }

    public sealed class ProtectedRow : MagicTableTool<ProtectedRow>, IMagicTable<object>
    {
        public int Id { get; set; }
        [MagicEncrypt] public string Secret { get; set; } = "";
        [MagicNotMapped] public object Databases { get; } = new();
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
        public List<IMagicCompoundIndex>? GetCompoundIndexes() => [];
        public string GetTableName() => "protected";
        public IndexedDbSet GetDefaultDatabase() => new("protected");
    }

    public sealed class InvalidProtectedRow : MagicTableTool<InvalidProtectedRow>, IMagicTable<object>
    {
        public int Id { get; set; }
        [MagicEncrypt, MagicIndex] public string Secret { get; set; } = "";
        [MagicNotMapped] public object Databases { get; } = new();
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
        public List<IMagicCompoundIndex>? GetCompoundIndexes() => [];
        public string GetTableName() => "protected";
        public IndexedDbSet GetDefaultDatabase() => new("protected");
    }
}
