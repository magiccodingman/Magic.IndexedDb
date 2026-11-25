using Microsoft.JSInterop;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Extensions;
using Magic.IndexedDb.LinqTranslation.Interfaces;
using Magic.IndexedDb.LinqTranslation.Models;
using Magic.IndexedDb.Migrations;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Magic.IndexedDb.Factories;

internal class MagicDbFactory : IMagicIndexedDb, IMagicIndexedDbEncryptionLifecycle, IAsyncDisposable
{        
    // null value indicates that the factory is disposed
    Lazy<Task<IJSObjectReference>>? _jsModule;
    Lazy<Task<IndexedDbManager>> _magicJsManager;
    private readonly long _jsMessageSizeBytes;
    private readonly Dictionary<string, EncryptionConfiguration> _encryption;
    private readonly Dictionary<string, EncryptionUnlock> _unlocks;
    private readonly Lazy<IReadOnlyDictionary<string, ResolvedMagicDatabase>> _definitions;
    private readonly ConcurrentDictionary<string, string> _instanceBindings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task>> _readiness = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MagicMigrationSubscription> _migrationSubscriptions = new(StringComparer.Ordinal);
    private readonly IJSRuntime _runtime;
    private readonly bool _isDebug;
    private readonly Type[]? _tableTypes;
    private readonly Type[]? _repositoryTypes;
    private readonly IMagicEncryptionKeyProvider? _keyProvider;
    private readonly bool _protectedOnly;
    private readonly NumericStorageConfiguration _numericStorage;
    private readonly CancellationTokenSource _lifetime = new();
    public long JsMessageSizeBytes => _jsMessageSizeBytes;

    public MagicDbFactory(
        IJSRuntime jSRuntime, long jsMessageSizeBytes, bool isDebug,
        Type[]? tableTypes = null, Type[]? repositoryTypes = null,
        Dictionary<string, EncryptionConfiguration>? encryption = null, IMagicEncryptionKeyProvider? keyProvider = null, bool protectedOnly = false,
        NumericStorageConfiguration? numericStorage = null)
    {
        _jsMessageSizeBytes = jsMessageSizeBytes;
        _runtime = jSRuntime;
        _isDebug = isDebug;
        _tableTypes = tableTypes;
        _repositoryTypes = repositoryTypes;
        _keyProvider = keyProvider;
        _protectedOnly = protectedOnly;
        _numericStorage = numericStorage ?? new NumericStorageConfiguration([]);
        _encryption = (encryption ?? new(StringComparer.Ordinal)).ToDictionary(pair => pair.Key,
            pair => pair.Value.WithNumericStorage(_numericStorage.Mode(pair.Key)), StringComparer.Ordinal);
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

        _definitions = new(() =>
        {
            var options = new MagicSchemaOptions
            {
                ProtectedDatabases = _encryption.Keys.ToArray(),
                NumericModes = _numericStorage.Modes,
                RecordPolicies = _encryption.ToDictionary(pair => pair.Key,
                    pair => pair.Value.RecordPolicies, StringComparer.Ordinal)
            };
            var definitions = MagicSchemaResolver.Resolve(
                tableTypes ?? SchemaHelper.GetAllMagicTables()!.ToArray(),
                repositoryTypes ?? SchemaHelper.GetAllMagicRepositories()!.ToArray(), options)
                .ToDictionary(database => database.Definition.DatabaseName, StringComparer.Ordinal);
            if (_encryption.Keys.Any(name => !definitions.ContainsKey(name)))
                throw new MagicSchemaException("MAGDB3002", "An encryption policy names an unregistered database.");
            if (_numericStorage.Modes.Keys.Any(name => !definitions.ContainsKey(name)))
                throw new MagicSchemaException("MAGDB3002", "A numeric policy names an unregistered database.");
            return definitions;
        });

        var moduleLoader = this._jsModule;
        this._magicJsManager = new(async () =>
        {
            // Resolve and validate every declaration before importing browser code.
            var definitions = _definitions.Value;
            var jsModule = await moduleLoader.Value;
            _lifetime.Token.ThrowIfCancellationRequested();
            var routes = definitions.Keys.ToDictionary(name => name, name => PhysicalRoute(name, name), StringComparer.Ordinal);
            return new IndexedDbManager(jsModule, _jsMessageSizeBytes, routes, _lifetime.Token, _numericStorage.Modes);
        }, isThreadSafe: true);
    }

    private string PhysicalRoute(string definitionName, string instanceName) =>
        _encryption.TryGetValue(definitionName, out var encrypted) ? encrypted.Route :
        _numericStorage.Mode(definitionName) == MagicNumericStorageMode.Lossless
            ? NumericStorageConfiguration.PhysicalName(instanceName) : instanceName;

    private ResolvedMagicDatabase Definition(string name)
    {
        ObjectDisposedException.ThrowIf(_jsModule is null || _lifetime.IsCancellationRequested, this);
        if (_protectedOnly && !_encryption.ContainsKey(name))
            throw new MagicSchemaException("MAGDB3011", "The database is outside this protected context.");
        return _definitions.Value.TryGetValue(name, out var definition) ? definition :
            throw new MagicSchemaException("MAGDB3020", $"Database '{name}' is not registered. Register its repository explicitly.");
    }

    private async Task EnsureDatabaseAsync(ResolvedMagicDatabase resolved, string instanceName)
    {
        var definitionName = resolved.Definition.DatabaseName;
        var route = PhysicalRoute(definitionName, instanceName);
        var identity = MagicSchemaJson.Fingerprint(resolved.Schema) + "|" + resolved.Definition.ManifestUrl;
        if (_instanceBindings.GetOrAdd(route, identity) != identity)
            throw new MagicSchemaException("MAGDB3022", "This physical instance is already bound to another schema or manifest.");
        var initialization = _readiness.GetOrAdd(route, _ => new Lazy<Task>(() => CreateDatabaseAsync(resolved, instanceName), true));
        try { await initialization.Value; }
        catch
        {
            // Failed startup is retryable. A successful committed migration is never rolled back here.
            _readiness.TryRemove(new KeyValuePair<string, Lazy<Task>>(route, initialization));
            throw;
        }
    }

    private async Task CreateDatabaseAsync(ResolvedMagicDatabase resolved, string instanceName)
    {
        await _magicJsManager.Value;
        _lifetime.Token.ThrowIfCancellationRequested();
        var name = resolved.Definition.DatabaseName;
        var entries = resolved.TableTypes.Zip(resolved.StoreSchemas, (type, schema) => (Type: type, Schema: schema)).ToArray();
        if (_numericStorage.Mode(name) == MagicNumericStorageMode.Lossless)
        {
            if (entries.Any(entry => MagicConverterHelper.HasCustomConverter(entry.Type, new())))
                throw new InvalidOperationException("Lossless numeric storage requires mapped table members and cannot use a whole-table JSON converter. Use property converters or Legacy storage instead.");
            foreach (var entry in entries.Where(entry => entry.Schema.PrimaryKeyAuto))
                if (PropertyMappingCache.GetTypeOfTProperties(entry.Type).propertyEntries.Values.Any(property =>
                    property.PrimaryKey && MagicConverterHelper.HasCustomConverter(property.Property)))
                    throw new InvalidOperationException("A lossless auto-increment key cannot use a custom JSON converter. Use an explicit key instead.");
        }

        JsonElement payload;
        if (_encryption.TryGetValue(name, out var configuration))
            payload = configuration.CreateDatabase(entries);
        else if (_numericStorage.Mode(name) == MagicNumericStorageMode.Lossless)
            payload = NumericStorageConfiguration.CreateDatabase(instanceName, resolved.StoreSchemas);
        else
            payload = JsonSerializer.SerializeToElement(new DbStore
            {
                Name = instanceName,
                Version = 1,
                StoreSchemas = resolved.StoreSchemas.ToList()
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var document = JsonNode.Parse(payload.GetRawText())!.AsObject();
        document["migrationSchema"] = JsonNode.Parse(MagicSchemaJson.Serialize(resolved.Schema));
        document["migrationManifestUrl"] = resolved.Definition.ManifestUrl;
        payload = JsonSerializer.SerializeToElement(document);
        var module = await _jsModule!.Value;
        if (_encryption.ContainsKey(name) || _numericStorage.Mode(name) == MagicNumericStorageMode.Lossless)
            await EncryptionUnlock.InvokeAsync<bool>(module, IndexedDbFunctions.CREATE_LEGACY, true, payload);
        else
            await new MagicJsInvoke(module, _jsMessageSizeBytes).CallJsAsync(Cache.MagicDbJsImportPath,
                IndexedDbFunctions.CREATE_LEGACY, default, new TypedArgument<JsonElement>(payload));
    }

    internal MagicDbFactory CreateProtectedContext(Dictionary<string, EncryptionConfiguration> configurations) =>
        new(_runtime, _jsMessageSizeBytes, _isDebug, _tableTypes, _repositoryTypes,
            configurations, _keyProvider, protectedOnly: true, _numericStorage);

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

        foreach (var subscription in _migrationSubscriptions.Values)
            await subscription.DisposeAsync();
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
        cancellationToken.ThrowIfCancellationRequested();

        var jsModule = await this._jsModule.Value.WaitAsync(cancellationToken);
        var magicUtility = new MagicUtilities(jsModule, _jsMessageSizeBytes);
        return await magicUtility.GetStorageEstimateAsync(cancellationToken);
    }
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

    public async ValueTask<IMagicQuery<T>> QueryOverride<T>(string databaseNameOverride, string schemaNameOverride) 
        where T: class, IMagicTableBase, new ()
    {
        ObjectDisposedException.ThrowIf(this._jsModule is null || _lifetime.IsCancellationRequested, this);

        var definition = Definition(databaseNameOverride);
        if (!definition.TableTypes.Contains(typeof(T)) || SchemaHelper.GetTableName<T>() != schemaNameOverride)
            throw new MagicSchemaException("MAGDB3021", $"Table '{typeof(T).Name}' does not belong to database '{databaseNameOverride}'. Use Include<T>() for an additional placement.");
        await EnsureDatabaseAsync(definition, databaseNameOverride);
        var dbManager = await _magicJsManager.Value;
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
            await EnsureDatabaseAsync(Definition(database.DatabaseName), database.DatabaseName);
            return module;
        }, initialize, cancellationToken);
    }

    public async Task<MagicEncryptionState> GetStateAsync(IndexedDbSet database, CancellationToken cancellationToken = default)
    {
        var js = _jsModule;
        if (js is null || _lifetime.IsCancellationRequested || !_encryption.TryGetValue(database.DatabaseName, out var configuration))
            return MagicEncryptionState.Inactive;

        var module = await js.Value.WaitAsync(cancellationToken);
        await EnsureDatabaseAsync(Definition(database.DatabaseName), database.DatabaseName).WaitAsync(cancellationToken);
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
                await EnsureDatabaseAsync(Definition(database.DatabaseName), database.DatabaseName);
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

    public ValueTask<IMagicDatabaseScoped> Database(IndexedDbSet indexedDbSet) =>
        Database(indexedDbSet, indexedDbSet.DatabaseName);

    public async ValueTask<IMagicDatabaseScoped> Database(IndexedDbSet indexedDbSet, string instanceName)
    {
        ArgumentNullException.ThrowIfNull(indexedDbSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        var definition = Definition(indexedDbSet.DatabaseName);
        if (definition.Definition.SchemaId != indexedDbSet.SchemaId || definition.Definition.ManifestUrl != indexedDbSet.ManifestUrl)
            throw new MagicSchemaException("MAGDB3022", "Use the registered database declaration; its schema identity and manifest cannot be overridden at query time.");
        if (instanceName.StartsWith("__magic_", StringComparison.Ordinal))
            throw new ArgumentException("Physical instances cannot use a reserved Magic namespace.", nameof(instanceName));
        if (instanceName != indexedDbSet.DatabaseName && _encryption.ContainsKey(indexedDbSet.DatabaseName))
            throw new MagicSchemaException("MAGDB3023", "Protected instances are account-scoped. Create an encryption context for the account instead.");
        if (instanceName != indexedDbSet.DatabaseName && _definitions.Value.ContainsKey(instanceName))
            throw new MagicSchemaException("MAGDB3022", "A physical instance cannot impersonate another registered database.");

        var manager = await _magicJsManager.Value;
        if (instanceName != indexedDbSet.DatabaseName)
            manager = new IndexedDbManager(await _jsModule!.Value, _jsMessageSizeBytes,
                new Dictionary<string, string> { [indexedDbSet.DatabaseName] = PhysicalRoute(indexedDbSet.DatabaseName, instanceName) },
                _lifetime.Token, _numericStorage.Modes);
        var route = PhysicalRoute(indexedDbSet.DatabaseName, instanceName);
        var physicalName = _encryption.TryGetValue(indexedDbSet.DatabaseName, out var encrypted) ? encrypted.PhysicalName : route;
        return new MagicDatabaseScoped(manager, indexedDbSet, definition.TableTypes,
            () => EnsureDatabaseAsync(definition, instanceName),
            () => _readiness.TryRemove(route, out _),
            async token => await (await _jsModule!.Value).InvokeAsync<JsonElement?>("inspectMigration", token, physicalName),
            async () => await (await _jsModule!.Value).InvokeVoidAsync("cancelMigration", route));
    }

    public async Task<IAsyncDisposable> ObserveMigrationsAsync(Func<MagicMigrationEvent, ValueTask> observer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ObjectDisposedException.ThrowIf(_jsModule is null || _lifetime.IsCancellationRequested, this);
        var subscription = new MagicMigrationSubscription(await _jsModule.Value.WaitAsync(cancellationToken), observer,
            id => _migrationSubscriptions.TryRemove(id, out _));
        _migrationSubscriptions[subscription.Id] = subscription;
        await subscription.StartAsync(cancellationToken);
        return subscription;
    }
}
