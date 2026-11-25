using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;

public sealed class ProtectedRepository : IMagicRepository
{
    public const string PhysicalName = "__magic_protected__:published-migration-fixture";
    public static readonly IndexedDbSet Records = new IndexedDbSet("protected-wasm")
        .WithMigrations("releases/final/protected-wasm/manifest.json");
}

// The final model intentionally contains no encrypted attributes. The account
// context and authenticated history remain after explicit declassification.
public sealed class ProtectedRow : MagicTableTool<ProtectedRow>, IMagicTable<ProtectedRepository>
{
    public ProtectedRepository Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => ProtectedRepository.Records;
    public string GetTableName() => "records";
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Secret { get; set; } = "";
    public int Visits { get; set; }
}
