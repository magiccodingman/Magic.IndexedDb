using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;

public sealed class RecoveryRepository : IMagicRepository
{
    public static readonly IndexedDbSet Records = new IndexedDbSet("recovery-wasm")
        .WithMigrations("releases/final/recovery-wasm/manifest.json");
}

public sealed class RecoveryRow : MagicTableTool<RecoveryRow>, IMagicTable<RecoveryRepository>
{
    public RecoveryRepository Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => RecoveryRepository.Records;
    public string GetTableName() => "records";
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];

    public int Id { get; set; }
    public int Value { get; set; }
    public int Repairs { get; set; }
    public bool Normalized { get; set; }
}
