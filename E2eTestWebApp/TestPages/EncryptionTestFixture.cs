using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;

namespace E2eTestWebApp.TestPages;

public sealed class EncryptionTestRepository : IMagicRepository
{
    public static readonly IndexedDbSet Database = new("EncryptionBrowserTest");
}

public sealed class EncryptionTestRecord : MagicTableTool<EncryptionTestRecord>, IMagicTable<EncryptionTestRepository>
{
    public string Id { get; set; } = "";
    [MagicIndex]
    public string Group { get; set; } = "";
    [MagicEncrypt, MagicName("protectedNote")]
    public string Note { get; set; } = "";

    public EncryptionTestRepository Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => EncryptionTestRepository.Database;
    public string GetTableName() => nameof(EncryptionTestRecord);
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(record => record.Id, autoIncrement: false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
}

public sealed class FixtureEncryptionKeyProvider : IMagicEncryptionKeyProvider
{
    public int Requests { get; private set; }

    public ValueTask<MagicEncryptionKey?> GetKeyAsync(
        MagicEncryptionKeyRequest request, CancellationToken cancellationToken)
    {
        Requests++;
        return ValueTask.FromResult<MagicEncryptionKey?>(MagicEncryptionKey.FromBytes(new byte[32]));
    }
}
