using System.Text.Json.Serialization;

namespace Magic.IndexedDb;

public class DbStore
{
    public string Name { get; set; }
    public int Version { get; set; }

    [JsonPropertyName("storeSchemas")]
    public List<StoreSchema> StoreSchemas { get; set; }

    [Obsolete("NOT SUPPORTED: This will likely be created to work with the Truth Protocol. Stay tuned.")]
    public List<DbMigration> DbMigrations { get; set; } = new List<DbMigration>();
}