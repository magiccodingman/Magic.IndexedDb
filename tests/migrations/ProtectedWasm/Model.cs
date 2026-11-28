using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;

public sealed class ProtectedRepository : IMagicRepository
{
    public const string RotationPhysicalName = "__magic_protected__:published-rotation-fixture";
    public static readonly IndexedDbSet RotationRecords = new IndexedDbSet("rotation-wasm")
        .WithMigrations("releases/rotation/rotation-wasm/manifest.json");
    public const string PhysicalName = "__magic_protected__:published-migration-fixture";
    public static readonly IndexedDbSet Records = new IndexedDbSet("protected-wasm")
        .WithMigrations("releases/final/protected-wasm/manifest.json");
}

// The final model intentionally contains no encrypted attributes. The account
// context and authenticated history remain after explicit declassification.
public sealed class ProtectedRow : MagicTableTool<ProtectedRow>, IMagicTable<ProtectedRepository>
{
    [MagicNotMapped]
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

[MagicEncryptRecord(nameof(Id))]
public sealed class RotationRow : MagicTableTool<RotationRow>, IMagicTable<ProtectedRepository>
{
    [MagicNotMapped]
    public ProtectedRepository Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => ProtectedRepository.RotationRecords;
    public string GetTableName() => "rotation-records";
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
    public int Id { get; set; }
    public int Revision { get; set; }
    public int Generation { get; set; }
    public string Secret { get; set; } = "";
}
