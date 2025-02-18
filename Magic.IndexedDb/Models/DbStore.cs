namespace Magic.IndexedDb
{
    public sealed class DbStore
    {
        public string Name { get; set; } = "client";
        public int Version { get; set; } = 1;
        public string? EncryptionKey { get; set; } = null;
        public List<StoreSchema> StoreSchemas { get; set; } = new List<StoreSchema>();
        public List<DbMigration> DbMigrations { get; set; } = new List<DbMigration>();
    }
}
