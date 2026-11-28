using Magic.IndexedDb.Factories;

namespace Magic.IndexedDb;

/// <summary>An account-bound view of the database and encryption APIs.</summary>
public sealed record MagicEncryptionContext(IMagicIndexedDb Database, IMagicIndexedDbEncryptionLifecycle Encryption)
{
    public IMagicIndexedDbKeyRotation KeyRotation => Encryption as IMagicIndexedDbKeyRotation
        ?? throw new InvalidOperationException("This encryption context does not support key rotation.");
}

/// <summary>Manages protected account contexts within an existing service scope.</summary>
public interface IMagicEncryptionContexts
{
    Task<MagicEncryptionContext> ActivateAsync(IEnumerable<MagicEncryptionOptions> databases);
    Task DeactivateAsync();
    Task DestroyAsync(IndexedDbSet database);
}

internal sealed class MagicEncryptionContexts(MagicDbFactory factory) : IMagicEncryptionContexts, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private MagicDbFactory? _active;
    private bool _disposed;

    public async Task<MagicEncryptionContext> ActivateAsync(IEnumerable<MagicEncryptionOptions> databases)
    {
        ArgumentNullException.ThrowIfNull(databases);
        var configurations = databases.Select(options => new EncryptionConfiguration(options))
            .ToDictionary(configuration => configuration.Request.DatabaseName, StringComparer.Ordinal);
        if (configurations.Count == 0)
            throw new ArgumentException("Activate at least one protected database.", nameof(databases));

        await _gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await DeactivateCoreAsync();
            var next = factory.CreateProtectedContext(configurations);
            _active = next;
            return new(next, next);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task DeactivateAsync() => EndContextAsync(database: null, dispose: false);

    public Task DestroyAsync(IndexedDbSet database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return EndContextAsync(database, dispose: false);
    }

    public async ValueTask DisposeAsync() => await EndContextAsync(database: null, dispose: true);

    private async Task EndContextAsync(IndexedDbSet? database, bool dispose)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = dispose;
            try
            {
                if (database is not null)
                {
                    var active = _active ?? throw new InvalidOperationException("No protected context is active.");
                    await active.DestroyProtectedDatabaseAsync(database);
                }
            }
            finally
            {
                await DeactivateCoreAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DeactivateCoreAsync()
    {
        var previous = _active;
        if (previous is not null)
            await previous.DeactivateProtectedContextAsync();
        _active = null;
    }
}
