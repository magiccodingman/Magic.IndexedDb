using System.Text.Json;
using System.Text.Json.Serialization;
using Magic.IndexedDb.Factories;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;
using Microsoft.JSInterop;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class NumericFactoryCompatibilityTests
{
    [TestMethod]
    public async Task LegacyConvertedVaultCanCoexistWithASeparateLosslessPlainDatabase()
    {
        var browser = new RecordingBrowser();
        await using var factory = Factory(browser, losslessVault: false);

        var plainQuery = await factory.Query<PlainRecord>();
        var vaultQuery = await factory.Query<ConvertedVaultRecord>();
        Assert.AreEqual(PlainRepository.Database.DatabaseName, plainQuery.DatabaseName);
        Assert.AreEqual(VaultRepository.Database.DatabaseName, vaultQuery.DatabaseName);
        Assert.AreEqual(2, browser.CreatedDatabases.Count);

        var plain = browser.CreatedDatabases.Single(payload => !payload.TryGetProperty("encryption", out _));
        var vault = browser.CreatedDatabases.Single(payload => payload.TryGetProperty("encryption", out _));
        Assert.AreEqual("lossless", plain.GetProperty("numericStorage").GetProperty("mode").GetString());
        Assert.IsFalse(vault.TryGetProperty("numericStorage", out _), "The Legacy vault must retain its existing format.");
        CollectionAssert.AreEqual(new[] { nameof(PlainRecord) }, plain.GetProperty("storeSchemas").EnumerateArray()
            .Select(schema => schema.GetProperty("tableName").GetString()).ToArray());
        var protectedSchema = vault.GetProperty("storeSchemas").EnumerateArray()
            .Single(schema => schema.GetProperty("tableName").GetString() == nameof(ConvertedVaultRecord));
        CollectionAssert.AreEqual(new[] { "id" }, protectedSchema.GetProperty("exposedFields").EnumerateArray()
            .Select(field => field.GetString()).ToArray());
    }

    [TestMethod]
    public async Task WholeTableConverterIsStillRejectedWhenItsProtectedDatabaseIsLossless()
    {
        var browser = new RecordingBrowser();
        await using var factory = Factory(browser, losslessVault: true);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await factory.Query<ConvertedVaultRecord>();
        });
        StringAssert.Contains(error.Message, "whole-table JSON converter");
        Assert.IsFalse(browser.CreatedDatabases.Any(payload => payload.TryGetProperty("encryption", out _)),
            "An unsupported Lossless vault must be rejected before it is opened in the browser.");
    }

    private static MagicDbFactory Factory(RecordingBrowser browser, bool losslessVault)
    {
        var policies = new List<MagicNumericStorageOptions> { new(PlainRepository.Database.DatabaseName) };
        if (losslessVault) policies.Add(new(VaultRepository.Database.DatabaseName));
        return new MagicDbFactory(browser, 1024, false,
            tableTypes: [typeof(PlainRecord), typeof(ConvertedVaultRecord)],
            repositoryTypes: [typeof(PlainRepository), typeof(VaultRepository)],
            encryption: new Dictionary<string, EncryptionConfiguration>
            {
                [VaultRepository.Database.DatabaseName] = new(new(VaultRepository.Database.DatabaseName, "test-account", "test-key"))
            },
            keyProvider: new UnusedKeyProvider(),
            numericStorage: new NumericStorageConfiguration(policies));
    }

    private sealed class RecordingBrowser : IJSRuntime, IJSObjectReference
    {
        public List<JsonElement> CreatedDatabases { get; } = [];

        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) =>
            InvokeAsync<T>(identifier, default, args);

        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            switch (identifier)
            {
                case "import":
                    return ValueTask.FromResult((T)(object)this);
                case "configureDebug":
                case IndexedDbFunctions.CLOSE_ALL:
                    return ValueTask.FromResult(default(T)!);
                case "JsHandler":
                    var method = (string)args![2]!;
                    if (method == IndexedDbFunctions.CREATE_LEGACY)
                    {
                        var parameters = (object?[])args[3]!;
                        CreatedDatabases.Add(((JsonElement)parameters[0]!).Clone());
                    }
                    else
                    {
                        Assert.AreEqual("releaseProtectedDatabase", method);
                    }
                    return ValueTask.FromResult((T)(object)true);
                default:
                    throw new InvalidOperationException($"Unexpected JS call '{identifier}'.");
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UnusedKeyProvider : IMagicEncryptionKeyProvider
    {
        public ValueTask<MagicEncryptionKey?> GetKeyAsync(MagicEncryptionKeyRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Schema initialization must not request an encryption key.");
    }

    public sealed class PlainRepository : IMagicRepository
    {
        public static readonly IndexedDbSet Database = new("Numeric.Factory.Plain");
    }

    public sealed class VaultRepository : IMagicRepository
    {
        public static readonly IndexedDbSet Database = new("Numeric.Factory.Vault");
    }

    public sealed class PlainRecord : MagicTableTool<PlainRecord>, IMagicTable<PlainRepository>
    {
        public int Id { get; set; }
        public long Value { get; set; }
        public PlainRepository Databases { get; } = new();
        public IndexedDbSet GetDefaultDatabase() => PlainRepository.Database;
        public string GetTableName() => nameof(PlainRecord);
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
        public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
    }

    [MagicEncryptRecord(nameof(Id))]
    [JsonConverter(typeof(VaultConverter))]
    public sealed class ConvertedVaultRecord : MagicTableTool<ConvertedVaultRecord>, IMagicTable<VaultRepository>
    {
        [MagicName("id")]
        public int Id { get; set; }
        public string Secret { get; set; } = "";
        public VaultRepository Databases { get; } = new();
        public IndexedDbSet GetDefaultDatabase() => VaultRepository.Database;
        public string GetTableName() => nameof(ConvertedVaultRecord);
        public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
        public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
    }

    public sealed class VaultConverter : JsonConverter<ConvertedVaultRecord>
    {
        public override ConvertedVaultRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return new()
            {
                Id = document.RootElement.GetProperty("id").GetInt32(),
                Secret = document.RootElement.GetProperty("payload").GetString()!
            };
        }

        public override void Write(Utf8JsonWriter writer, ConvertedVaultRecord value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", value.Id);
            writer.WriteString("payload", value.Secret);
            writer.WriteEndObject();
        }
    }
}
