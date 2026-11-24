using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;
using Microsoft.JSInterop;

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

public sealed class FixtureEncryptionKeyProvider(IJSRuntime runtime) : IMagicEncryptionKeyProvider, IAsyncDisposable
{
    private IJSObjectReference? _browserKey;
    public int Requests { get; private set; }
    public bool UseBrowserKey { get; set; }

    public async ValueTask<MagicEncryptionKey?> GetKeyAsync(
        MagicEncryptionKeyRequest request, CancellationToken cancellationToken)
    {
        Requests++;
        if (!UseBrowserKey)
            return MagicEncryptionKey.FromBytes(new byte[32]);

        if (_browserKey is null)
        {
            await using var module = await runtime.InvokeAsync<IJSObjectReference>(
                "import", cancellationToken, "./encryptionFixture.js");
            _browserKey = await module.InvokeAsync<IJSObjectReference>("createFixtureBrowserKey", cancellationToken);
        }
        return MagicEncryptionKey.FromBrowserKey(_browserKey);
    }

    public async ValueTask DisposeAsync()
    {
        if (_browserKey is not null)
        {
            try { await _browserKey.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}
