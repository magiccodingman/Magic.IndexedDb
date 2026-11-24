using Microsoft.JSInterop;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Extensions;
using Magic.IndexedDb.LinqTranslation.Interfaces;

namespace Magic.IndexedDb.Factories;

internal class MagicDbFactory : IMagicIndexedDb, IMagicIndexedDbEncryptionLifecycle, IAsyncDisposable
{        
    // null value indicates that the factory is disposed
    Lazy<Task<IJSObjectReference>>? _jsModule;
    Lazy<Task<IndexedDbManager>> _magicJsManager;
    private readonly long _jsMessageSizeBytes;
    private readonly Dictionary<string, EncryptionConfiguration> _encryption;
    private readonly Dictionary<string, EncryptionUnlock> _unlocks;
    private IReadOnlyDictionary<string, Type>? _encryptedSchemaTypes;
    private readonly IJSRuntime _runtime;
    private readonly bool _isDebug;
    private readonly Type[]? _tableTypes;
    private readonly Type[]? _repositoryTypes;
    private readonly IMagicEncryptionKeyProvider? _keyProvider;
    private readonly bool _protectedOnly;
    private readonly CancellationTokenSource _lifetime = new();
    public long JsMessageSizeBytes => _jsMessageSizeBytes;

    public MagicDbFactory(
        IJSRuntime jSRuntime, long jsMessageSizeBytes, bool isDebug,
        Type[]? tableTypes = null, Type[]? repositoryTypes = null,
        Dictionary<string, EncryptionConfiguration>? encryption = null, IMagicEncryptionKeyProvider? keyProvider = null, bool protectedOnly = false)
    {
        _jsMessageSizeBytes = jsMessageSizeBytes;
        _runtime = jSRuntime;
        _isDebug = isDebug;
        _tableTypes = tableTypes;
        _repositoryTypes = repositoryTypes;
        _keyProvider = keyProvider;
        _protectedOnly = protectedOnly;
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

        var moduleLoader = this._jsModule;
        this._magicJsManager = new(async () =>
            {
                var jsModule = await moduleLoader.Value;
                _lifetime.Token.ThrowIfCancellationRequested();

                var types = tableTypes ?? SchemaHelper.GetAllMagicTables()!.ToArray();
                var schemas = types.Select(type => (Type: type, Schema: SchemaHelper.GetStoreSchema(type))).ToArray();
                if (_encryption.Count > 0)
                    _encryptedSchemaTypes = schemas.ToDictionary(entry => entry.Schema.TableName, entry => entry.Type, StringComparer.Ordinal);
                var plainSchemas = schemas.Where(entry => !EncryptionConfiguration.RequiresProtection(entry.Type))
                    .Select(entry => entry.Schema).ToList();
                var manager = new IndexedDbManager(jsModule, _jsMessageSizeBytes,
                    _encryption.ToDictionary(pair => pair.Key, pair => pair.Value.Route, StringComparer.Ordinal), _lifetime.Token);

                var dbSets = repositoryTypes is null
                    ? SchemaHelper.GetAllIndexedDbSets()
                    : SchemaHelper.GetIndexedDbSets(repositoryTypes);
                var names = dbSets.Select(set => set.DatabaseName).ToHashSet(StringComparer.Ordinal);
                if (_encryption.Keys.Any(name => !names.Contains(name)))
                    throw new InvalidOperationException("A Magic encryption policy names an unregistered database.");

                foreach (var name in names)
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                    if (_encryption.TryGetValue(name, out var configuration))
                    {
                        await EncryptionUnlock.InvokeAsync<bool>(jsModule, IndexedDbFunctions.CREATE_LEGACY, true,
                            configuration.CreateDatabase(schemas));
                        continue;
                    }

                    if (_protectedOnly) continue;
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

    internal MagicDbFactory CreateProtectedContext(Dictionary<string, EncryptionConfiguration> configurations) =>
        new(_runtime, _jsMessageSizeBytes, _isDebug, _tableTypes, _repositoryTypes,
            configurations, _keyProvider, protectedOnly: true);

    internal async Task DestroyProtectedDatabaseAsync(IndexedDbSet database)
    {
        var unlock = GetUnlock(database);
        var configuration = _encryption[database.DatabaseName];
        var js = _jsModule;
        ObjectDisposedException.ThrowIf(js is null || _lifetime.IsCancellationRequested, this);
        _lifetime.Cancel();
        await unlock.LockAsync(() => js.Value, dispose: true);
        if (_magicJsManager.IsValueCreated)
        {
            try { await _magicJsManager.Value; }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        }
        await EncryptionUnlock.InvokeAsync<bool>(await js.Value, "destroyProtectedDatabase", true, configuration.PhysicalName);
    }

    public ValueTask DisposeAsync() => DisposeCoreAsync(requireBrowserRelease: false);

    internal ValueTask DeactivateProtectedContextAsync() => DisposeCoreAsync(requireBrowserRelease: true);

    private async ValueTask DisposeCoreAsync(bool requireBrowserRelease)
    {
        var js = _jsModule;
        _lifetime.Cancel();
        if (!requireBrowserRelease)
            _jsModule = null;

        if (js is null || !js.IsValueCreated)
        {
            _jsModule = null;
            return;
        }

        IJSObjectReference module;
        try
        {
            module = await js.Value;
        }
        catch when (!requireBrowserRelease)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var releases = _unlocks.Values.Select(unlock => unlock.LockAsync(async () =>
            {
                if (_magicJsManager.IsValueCreated)
                {
                    try { await _magicJsManager.Value; }
                    catch { /* Release any routes that were created before the failure. */ }
                }
                return module;
            }, dispose: true));
            await Task.WhenAll(releases).WaitAsync(timeout.Token);

            if (_unlocks.Count == 0 && !_protectedOnly)
                await module.InvokeVoidAsync(IndexedDbFunctions.CLOSE_ALL, timeout.Token);
        }
        catch when (!requireBrowserRelease)
        {
            // Disposal from the DI container may run after a browser disconnect.
        }

        _jsModule = null;
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
        ObjectDisposedException.ThrowIf(this._jsModule is null || _lifetime.IsCancellationRequested, this);

        var jsModule = await this._jsModule.Value;
        var magicUtility = new MagicUtilities(jsModule, _jsMessageSizeBytes);
        return await magicUtility.GetStorageEstimateAsync();
    }
    [Obsolete("Not fully implemented yet until full migration protocol finished.")]
    public async ValueTask<IMagicQuery<T>> Query<T>(IndexedDbSet indexedDbSet)
        where T : class, IMagicTableBase, new()
    {
        ObjectDisposedException.ThrowIf(this._jsModule is null || _lifetime.IsCancellationRequested, this);

        // Get database name and schema name
        string databaseName = indexedDbSet.DatabaseName;
        string schemaName = SchemaHelper.GetTableName<T>();

        return await QueryOverride<T>(databaseName, schemaName);
    }


    public async ValueTask<IMagicQuery<T>> Query<T>(
        Func<T, IndexedDbSet> dbSetSelector)
        where T : class, IMagicTableBase, new()
    {
        ObjectDisposedException.ThrowIf(this._jsModule is null || _lifetime.IsCancellationRequested, this);

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
        ObjectDisposedException.ThrowIf(this._jsModule is null || _lifetime.IsCancellationRequested, this);

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
        ObjectDisposedException.ThrowIf(this._jsModule is null || _lifetime.IsCancellationRequested, this);

        if (!_encryption.ContainsKey(databaseNameOverride) && (_protectedOnly || EncryptionConfiguration.RequiresProtection(typeof(T))))
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
        ObjectDisposedException.ThrowIf(js is null || _lifetime.IsCancellationRequested, this);
        return GetUnlock(database).UnlockAsync(async () =>
        {
            var module = await js.Value;
            await _magicJsManager.Value;
            return module;
        }, initialize, cancellationToken);
    }

    public async Task<MagicEncryptionState> GetStateAsync(IndexedDbSet database, CancellationToken cancellationToken = default)
    {
        var js = _jsModule;
        if (js is null || _lifetime.IsCancellationRequested || !_encryption.TryGetValue(database.DatabaseName, out var configuration))
            return MagicEncryptionState.Inactive;

        var module = await js.Value.WaitAsync(cancellationToken);
        await _magicJsManager.Value.WaitAsync(cancellationToken);
        var state = await EncryptionUnlock.InvokeAsync<string>(
            module, "getProtectedDatabaseState", false, configuration.Route).AsTask().WaitAsync(cancellationToken);
        return Enum.Parse<MagicEncryptionState>(state);
    }

    public async Task<MagicUnlockResult> UnlockDetailedAsync(IndexedDbSet database, bool initialize = false,
        CancellationToken cancellationToken = default)
    {
        var js = _jsModule;
        ObjectDisposedException.ThrowIf(js is null || _lifetime.IsCancellationRequested, this);
        try
        {
            return await GetUnlock(database).UnlockCoreAsync(async () =>
            {
                var module = await js.Value;
                await _magicJsManager.Value;
                return module;
            }, initialize, cancellationToken);
        }
        catch (MagicEncryptionInteractionRequiredException) { return MagicUnlockResult.InteractionRequired; }
        catch (OperationCanceledException) { return MagicUnlockResult.Cancelled; }
        catch (JSException error) when (error.Message.Contains("MAGIC_ENCRYPTION_UNINITIALIZED:", StringComparison.Ordinal))
        {
            return MagicUnlockResult.Uninitialized;
        }
        catch (JSException error) when (error.Message.Contains("MAGIC_ENCRYPTION_INVALID_KEY:", StringComparison.Ordinal))
        {
            return MagicUnlockResult.InvalidKey;
        }
        catch (JSException error) when (error.Message.Contains("MAGIC_ENCRYPTION_POLICY_MISMATCH:", StringComparison.Ordinal))
        {
            return MagicUnlockResult.PolicyMismatch;
        }
    }

    public Task LockAsync(IndexedDbSet database)
    {
        var js = _jsModule;
        ObjectDisposedException.ThrowIf(js is null || _lifetime.IsCancellationRequested, this);
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
        ObjectDisposedException.ThrowIf(this._jsModule is null || _lifetime.IsCancellationRequested, this);

        if (_protectedOnly && !_encryption.ContainsKey(indexedDbSet.DatabaseName))
            throw new InvalidOperationException("The database is outside this protected context.");
        var dbManager = await this._magicJsManager.Value;
        return dbManager.Database(dbManager, indexedDbSet);
    }
}
