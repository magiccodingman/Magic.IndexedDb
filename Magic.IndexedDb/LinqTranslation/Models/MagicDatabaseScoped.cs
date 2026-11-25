using System.Text.Json;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.LinqTranslation.Interfaces;
using Magic.IndexedDb.Migrations;

namespace Magic.IndexedDb.LinqTranslation.Models;

internal sealed class MagicDatabaseScoped : IMagicDatabaseScoped
{
    private readonly IndexedDbSet _definition;
    private readonly IndexedDbManager _manager;
    private readonly IReadOnlyList<Type> _tableTypes;
    private readonly Func<Task> _ensure;
    private readonly Action _invalidate;
    private readonly Func<CancellationToken, Task<JsonElement?>> _inspect;
    private readonly Func<Task> _cancel;
    private readonly Func<string, string, CancellationToken, Task<JsonElement>> _recover;

    internal MagicDatabaseScoped(IndexedDbManager manager, IndexedDbSet definition, IReadOnlyList<Type> tableTypes,
        Func<Task> ensure, Action invalidate, Func<CancellationToken, Task<JsonElement?>> inspect, Func<Task> cancel,
        Func<string, string, CancellationToken, Task<JsonElement>> recover)
    {
        _manager = manager;
        _definition = definition;
        _tableTypes = tableTypes;
        _ensure = ensure;
        _invalidate = invalidate;
        _inspect = inspect;
        _cancel = cancel;
        _recover = recover;
    }

    public async Task DeleteAsync()
    {
        await _manager.DeleteDbAsync(_definition.DatabaseName);
        _invalidate();
    }

    public Task CloseAsync() => _manager.CloseDbAsync(_definition.DatabaseName);
    public Task<bool> IsOpenAsync() => _manager.IsDbOpen(_definition.DatabaseName);
    public Task<bool> DoesExistAsync() => _manager.DoesDbExist(_definition.DatabaseName);
    public async Task OpenAsync()
    {
        await _ensure();
        await _manager.OpenDbAsync(_definition.DatabaseName);
    }

    public async ValueTask<IMagicQuery<T>> Query<T>() where T : class, IMagicTableBase, new()
    {
        if (!_tableTypes.Contains(typeof(T)))
            throw new MagicSchemaException("MAGDB3021", $"Table '{typeof(T).Name}' does not belong to schema '{_definition.SchemaId}'.");
        await _ensure();
        return _manager.Query<T>(_definition.DatabaseName, SchemaHelper.GetTableName<T>());
    }

    public Task<JsonElement?> InspectMigrationAsync(CancellationToken cancellationToken = default) => _inspect(cancellationToken);
    public async Task RetryMigrationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _invalidate();
        await _ensure().WaitAsync(cancellationToken);
    }
    public Task<JsonElement> RecoverMigrationAsync(string recoveryId, string sha256, CancellationToken cancellationToken = default)
        => _recover(recoveryId, sha256, cancellationToken);
    public Task CancelMigrationAsync() => _cancel();
}
