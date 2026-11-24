using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;

namespace TestWasm.Models;

public sealed class EncryptionBenchmarkRepository : IMagicRepository
{
    public static readonly IndexedDbSet Plain = new("EncryptionBenchmarkPlain");
    public static readonly IndexedDbSet Protected = new("EncryptionBenchmarkProtected");
    public readonly IndexedDbSet ProtectedSelection = Protected;
}

public sealed class EncryptionBenchmarkRecord : MagicTableTool<EncryptionBenchmarkRecord>, IMagicTable<EncryptionBenchmarkRepository>
{
    public int Id { get; set; }
    [MagicIndex]
    public int Group { get; set; }
    public string Payload { get; set; } = "";

    public EncryptionBenchmarkRepository Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => EncryptionBenchmarkRepository.Plain;
    public string GetTableName() => nameof(EncryptionBenchmarkRecord);
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, autoIncrement: false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
}

public sealed class BenchmarkKeyProvider : IMagicEncryptionKeyProvider
{
    // Synthetic fixture only. Reusing a hardcoded key would defeat deployment security.
    public ValueTask<MagicEncryptionKey?> GetKeyAsync(MagicEncryptionKeyRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<MagicEncryptionKey?>(MagicEncryptionKey.FromBytes(new byte[32]));
}
