namespace Magic.IndexedDb;

public class DbMigration
{
    public string? FromVersion { get; set; }
    public string? ToVersion { get; set; }
    public List<DbMigrationInstruction> Instructions { get; set; } = new List<DbMigrationInstruction>();
}