using System.Text.Json.Serialization;

namespace Magic.IndexedDb;

public class DbStore
{
    public string Name { get; set; }
    public int Version { get; set; }

    [JsonPropertyName("storeSchemas")]
    public List<StoreSchema> StoreSchemas { get; set; }

    [Obsolete("Magic IndexedDB 3 uses static migration manifests. Replace DbMigrations with IndexedDbSet.WithMigrations(...). See the v3 upgrade guide.", error: true, DiagnosticId = "MAGDB3000", UrlFormat = "https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/upgrading/v3.md#diagnostics")]
    [JsonIgnore]
    public List<DbMigration> DbMigrations { get; set; } = new List<DbMigration>();
}
