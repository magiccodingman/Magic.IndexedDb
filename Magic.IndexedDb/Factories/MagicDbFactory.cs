using Microsoft.JSInterop;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Extensions;
using Magic.IndexedDb.LinqTranslation.Interfaces;

namespace Magic.IndexedDb.Factories;

internal class MagicDbFactory : IMagicIndexedDb, IMagicIndexedDbEncryption, IAsyncDisposable
{        
    // null value indicates that the factory is disposed
    Lazy<Task<IJSObjectReference>>? _jsModule;
    Lazy<Task<IndexedDbManager>> _magicJsManager;
    private readonly long _jsMessageSizeBytes;
    private readonly Dictionary<string, EncryptionConfiguration> _encryption;
    private readonly Dictionary<string, EncryptionUnlock> _unlocks;
    private IReadOnlyDictionary<string, Type>? _encryptedSchemaTypes;
    public long JsMessageSizeBytes => _jsMessageSizeBytes;

    public MagicDbFactory(
        IJSRuntime jSRuntime, long jsMessageSizeBytes, bool isDebug,
        Type[]? tableTypes = null, Type[]? repositoryTypes = null,
        Dictionary<string, EncryptionConfiguration>? encryption = null, IMagicEncryptionKeyProvider? keyProvider = null)
    {
        _jsMessageSizeBytes = jsMessageSizeBytes;
        _encryption = encryption ?? new(StringComparer.Ordinal);
        _unlocks = _encryption.ToDictionary(pair => pair.Key,
            pair => new EncryptionUnlock(pair.Value, keyProvider!), StringComparer.Ordinal);
        this._jsModule = new(async () =>
            {
                var module = await jSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/Magic.IndexedDb/magicDbMethods.js");
                await module.InvokeVoidAsync("configureDebug", isDebug);
                return module;
            },
            isThreadSafe: true);

        this._magicJsManager = new(async () =>
            {
                var jsModule = await this._jsModule.Value;

                var types = tableTypes ?? SchemaHelper.GetAllMagicTables()!.ToArray();
                var schemas = types.Select(type => (Type: type, Schema: SchemaHelper.GetStoreSchema(type))).ToArray();
                if (_encryption.Count > 0)
                    _encryptedSchemaTypes = schemas.ToDictionary(entry => entry.Schema.TableName, entry => entry.Type, StringComparer.Ordinal);
                var plainSchemas = schemas.Where(entry => EncryptionConfiguration.ProtectedFields(entry.Type).Length == 0)
                    .Select(entry => entry.Schema).ToList();
                var manager = new IndexedDbManager(jsModule, _jsMessageSizeBytes,
                    _encryption.ToDictionary(pair => pair.Key, pair => pair.Value.Route, StringComparer.Ordinal));

                var dbSets = repositoryTypes is null
                    ? SchemaHelper.GetAllIndexedDbSets()
                    : SchemaHelper.GetIndexedDbSets(repositoryTypes);
                var names = dbSets.Select(set => set.DatabaseName).ToHashSet(StringComparer.Ordinal);
                if (_encryption.Keys.Any(name => !names.Contains(name)))
                    throw new InvalidOperationException("A Magic encryption policy names an unregistered database.");

                foreach (var name in names)
                {
                    if (_encryption.TryGetValue(name, out var configuration))
                    {
                        await EncryptionUnlock.InvokeAsync<bool>(jsModule, IndexedDbFunctions.CREATE_LEGACY, true,
                            configuration.CreateDatabase(schemas));
                        continue;
                    }

                    await new MagicJsInvoke(jsModule, _jsMessageSizeBytes).CallJsAsync(Cache.MagicDbJsImportPath,
                        IndexedDbFunctions.CREATE_LEGACY, default,
                        new TypedArgument<DbStore>(new DbStore()
                        {
                            Name = name,
                            Version = 1,
                            StoreSchemas = plainSchemas
                        }));
                }
                return manager;
            },
            isThreadSafe: true);
    }

    public async ValueTask DisposeAsync()
    {
        var js = _jsModule;
        _jsModule = null;

        if (js is null || !js.IsValueCreated)
            return;

        IJSObjectReference module;
        try
        {
            module = await js.Value;
        }
        catch
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            foreach (var unlock in _unlocks.Values)
                await unlock.LockAsync(async () =>
                {
                    if (_magicJsManager.IsValueCreated)
                    {
                        try { await _magicJsManager.Value; }
                        catch { /* Release any routes that were created before the failure. */ }
                    }
                    return module;
                }, dispose: true).WaitAsync(timeout.Token);

            if (_unlocks.Count == 0)
                await module.InvokeVoidAsync(IndexedDbFunctions.CLOSE_ALL, timeout.Token);
        }
        catch
        {
            // do nothing
        }

        try
        {
            await module.DisposeAsync();
        }
        catch
        {
            // do nothing
        }
    }

    /// <summary>
    /// Get storage estimate using the shared JS module.
    /// </summary>
    public async Task<QuotaUsage> GetStorageEstimateAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this._jsModule is null, this);

        var jsModule = await this._jsModule.Value;
        var magicUtility = new MagicUtilities(jsModule, _jsMessageSizeBytes);
        return await magicUtility.GetStorageEstimateAsync();
    }
    [Obsolete("Not fully implemented yet until full migration protocol finished.")]
    public async ValueTask<IMagicQuery<T>> Query<T>(IndexedDbSet indexedDbSet)
        where T : class, IMagicTableBase, new()
    {
        ObjectDisposedException.ThrowIf(this._jsModule is null, this);

        // Get database name and schema name
        string databaseName = indexedDbSet.DatabaseName;
        string schemaName = SchemaHelper.GetTableName<T>();

        return await QueryOverride<T>(databaseName, schemaName);
    }


    public async ValueTask<IMagicQuery<T>> Query<T>(
        Func<T, IndexedDbSet> dbSetSelector)
        where T : class, IMagicTableBase, new()
    {
        ObjectDisposedException.ThrowIf(this._jsModule is null, this);

        // Create an instance of T to access `DbSets`
        var modelInstance = new T();

        // Retrieve the IndexedDbSet using the provided predicate
        IndexedDbSet selectedDbSet = dbSetSelector(modelInstance);

        // Get database name and schema name
        string databaseName = selectedDbSet.DatabaseName;
        string schemaName = SchemaHelper.GetTableName<T>();

#pragma warning disable CS0618
        return await QueryOverride<T>(databaseName, schemaName);
#pragma warning restore CS0618
    }

    /// <summary>
    /// Query the database for a given type. Automatically opens the database if needed.
    /// </summary>
    public async ValueTask<IMagicQuery<T>> Query<T>() 
        where T : class, IMagicTableBase, new()
    {
        ObjectDisposedException.ThrowIf(this._jsModule is null, this);

        string databaseName = SchemaHelper.GetDefaultDatabaseName<T>();
        string schemaName = SchemaHelper.GetTableName<T>();
#pragma warning disable CS0618
        return await QueryOverride<T>(databaseName, schemaName);
#pragma warning restore CS0618
    }

    [Obsolete("Not decided if this will be built in further or removed")]
    public async ValueTask<IMagicQuery<T>> QueryOverride<T>(string databaseNameOverride, string schemaNameOverride) 
        where T: class, IMagicTableBase, new ()
    {
        ObjectDisposedException.ThrowIf(this._jsModule is null, this);

        if (!_encryption.ContainsKey(databaseNameOverride) && EncryptionConfiguration.ProtectedFields(typeof(T)).Length > 0)
            throw new InvalidOperationException("The model has protected members and requires a Magic encryption policy.");

        var dbManager = await this._magicJsManager.Value;
        if (_encryption.ContainsKey(databaseNameOverride) &&
            _encryptedSchemaTypes?.GetValueOrDefault(schemaNameOverride) != typeof(T))
            throw new InvalidOperationException("A protected database query requires its registered table type.");
        return dbManager.Query<T>(databaseNameOverride, schemaNameOverride);
    }
        
    public Task<bool> InitializeAsync(IndexedDbSet database, CancellationToken cancellationToken = default) =>
        UnlockAsync(database, initialize: true, cancellationToken);

    public Task<bool> EnsureUnlockedAsync(IndexedDbSet database, CancellationToken cancellationToken = default) =>
        UnlockAsync(database, initialize: false, cancellationToken);

    private Task<bool> UnlockAsync(IndexedDbSet database, bool initialize, CancellationToken cancellationToken)
    {
        var js = _jsModule;
        ObjectDisposedException.ThrowIf(js is null, this);
        return GetUnlock(database).UnlockAsync(async () =>
        {
            var module = await js.Value;
            await _magicJsManager.Value;
            return module;
        }, initialize, cancellationToken);
    }

    public Task LockAsync(IndexedDbSet database)
    {
        var js = _jsModule;
        ObjectDisposedException.ThrowIf(js is null, this);
        return GetUnlock(database).LockAsync(() => js.Value);
    }

    private EncryptionUnlock GetUnlock(IndexedDbSet database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return _unlocks.TryGetValue(database.DatabaseName, out var unlock)
            ? unlock
            : throw new InvalidOperationException("This database does not have a Magic encryption policy.");
    }

    public async ValueTask<IMagicDatabaseScoped> Database(IndexedDbSet indexedDbSet)
    {
        ObjectDisposedException.ThrowIf(this._jsModule is null, this);

        var dbManager = await this._magicJsManager.Value;
        return dbManager.Database(dbManager, indexedDbSet);
    }
}
