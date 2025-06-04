using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Extensions;
using System.Collections.Concurrent;
using System.Reflection;
using Magic.IndexedDb.LinqTranslation.Interfaces;
using System.Diagnostics;
using System.Threading;

namespace Magic.IndexedDb.Factories;

internal class MagicDbFactory : IMagicIndexedDb, IAsyncDisposable
{        
    // null value indicates that the factory is disposed
    Lazy<Task<IJSObjectReference>>? _jsModule;
    Lazy<Task<IndexedDbManager>> _magicJsManager;
    private readonly long _jsMessageSizeBytes;
    public long JsMessageSizeBytes => _jsMessageSizeBytes;

    public MagicDbFactory(IJSRuntime jSRuntime, long jsMessageSizeBytes)
    {
        _jsMessageSizeBytes = jsMessageSizeBytes;
        this._jsModule = new(() => jSRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Magic.IndexedDb/magicDbMethods.js").AsTask(),
            isThreadSafe: true);

        this._magicJsManager = new(async () =>
            {
                var jsModule = await this._jsModule.Value;

                var dbSchemas = SchemaHelper.GetAllSchemas();
                // Create & Open the database (formerly in IndexedDbManager)
                var manager = new IndexedDbManager(jsModule, _jsMessageSizeBytes);

                var dbSets = SchemaHelper.GetAllIndexedDbSets();

                if (dbSets != null)
                {
                    foreach (var dbSet in dbSets)
                    {
                        await new MagicJsInvoke(jsModule, _jsMessageSizeBytes).CallJsAsync(Cache.MagicDbJsImportPath,
                            IndexedDbFunctions.CREATE_LEGACY, default,
                            new TypedArgument<DbStore>(new DbStore()
                            {
                                Name = dbSet.DatabaseName,
                                Version = 1,
                                StoreSchemas = dbSchemas
                            }));
                    }
                }
                else
                {
                    Console.WriteLine("No IndexedDbSet found and/or no found IMagicRepository.");
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
            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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
        var dbManager = await this._magicJsManager.Value;
#pragma warning disable CS0618
        return await QueryOverride<T>(databaseName, schemaName);
#pragma warning restore CS0618
    }

    [Obsolete("Not decided if this will be built in further or removed")]
    public async ValueTask<IMagicQuery<T>> QueryOverride<T>(string databaseNameOverride, string schemaNameOverride) 
        where T: class, IMagicTableBase, new ()
    {
        ObjectDisposedException.ThrowIf(this._jsModule is null, this);

        var dbManager = await this._magicJsManager.Value;
        return dbManager.Query<T>(databaseNameOverride, schemaNameOverride);
    }
        
    public async ValueTask<IMagicDatabaseScoped> Database(IndexedDbSet indexedDbSet)
    {
        ObjectDisposedException.ThrowIf(this._jsModule is null, this);

        var dbManager = await this._magicJsManager.Value;
        return dbManager.Database(dbManager, indexedDbSet);
    }
}