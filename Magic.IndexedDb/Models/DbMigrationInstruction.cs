namespace Magic.IndexedDb;

public class DbMigrationInstruction
{
    public string Action { get; set; }
    public string StoreName { get; set; }
    public string Details { get; set; }
}