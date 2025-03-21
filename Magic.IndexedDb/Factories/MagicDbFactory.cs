using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Extensions;
using System.Collections.Concurrent;
using System.Reflection;
using Magic.IndexedDb.LinqTranslation.Interfaces;

namespace Magic.IndexedDb.Factories
{
    internal class MagicDbFactory : IMagicIndexedDb, IAsyncDisposable
    {        
        internal MagicDbFactory(long jsMessageSizeBytes)
        {
            Cache.JsMessageSizeBytes = jsMessageSizeBytes;
        }

        Lazy<Task<IJSObjectReference>>? _jsRuntime;
        readonly IServiceProvider _serviceProvider;
        public static IndexedDbManager? _MagicJsManager { get; set; } = null;
        private IJSObjectReference? _cachedJsModule; // Shared JS module instance
        public MagicDbFactory(IServiceProvider serviceProvider, IJSRuntime jSRuntime, long jsMessageSizeBytes)
        {
            _serviceProvider = serviceProvider;
            this._jsRuntime = new(() => jSRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Magic.IndexedDb/magicDbMethods.js").AsTask());
        }

        /// <summary>
        /// Get or initialize the shared JavaScript module.
        /// </summary>
        private async Task<IJSObjectReference> GetJsModuleAsync()
        {
            if (_cachedJsModule is not null)
                return _cachedJsModule;

            _cachedJsModule = await _jsRuntime.Value;
            return _cachedJsModule;
        }

        public async ValueTask DisposeAsync()
        {
            var js = _jsRuntime;
            _jsRuntime = null;

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
        /// Ensure a database is opened and properly associated with the shared JS module.
        /// </summary>
        private async ValueTask<IndexedDbManager> GetOrCreateDatabaseAsync(CancellationToken cancellationToken = default)
        {
            if (_MagicJsManager != null)
                return _MagicJsManager; // Return cached instance

            var jsModule = await GetJsModuleAsync(); // Ensure shared JS module is ready

            var dbSchemas = SchemaHelper.GetAllSchemas();
            // Create & Open the database (formerly in IndexedDbManager)
            var manager = new IndexedDbManager(jsModule);

            var dbSets = SchemaHelper.GetAllIndexedDbSets();

            if (dbSets != null)
            {
                foreach (var dbSet in dbSets)
                {
                    await new MagicJsInvoke(jsModule).CallJsAsync(Cache.MagicDbJsImportPath,
                        IndexedDbFunctions.CREATE_LEGACY, cancellationToken,
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
                _MagicJsManager = manager; // Cache the opened database
            return _MagicJsManager;
        }


        /// <summary>
        /// Get storage estimate using the shared JS module.
        /// </summary>
        public async Task<QuotaUsage> GetStorageEstimateAsync(CancellationToken cancellationToken = default)
        {
            var jsModule = await GetJsModuleAsync(); // Shared JS module reference
            var magicUtility = new MagicUtilities(jsModule);
            return await magicUtility.GetStorageEstimateAsync();
        }
        [Obsolete("Not fully implemented yet until full migration protocol finished.")]
        public async ValueTask<IMagicQuery<T>> Query<T>(IndexedDbSet indexedDbSet)
    where T : class, IMagicTableBase, new()
        {
            // Get database name and schema name
            string databaseName = indexedDbSet.DatabaseName;
            string schemaName = SchemaHelper.GetTableName<T>();

            return await QueryOverride<T>(databaseName, schemaName);
        }


        public async ValueTask<IMagicQuery<T>> Query<T>(
    Func<T, IndexedDbSet> dbSetSelector)
    where T : class, IMagicTableBase, new()
        {
            // Create an instance of T to access `DbSets`
            var modelInstance = new T();

            // Retrieve the IndexedDbSet using the provided predicate
            IndexedDbSet selectedDbSet = dbSetSelector(modelInstance);

            // Get database name and schema name
            string databaseName = selectedDbSet.DatabaseName;
            string schemaName = SchemaHelper.GetTableName<T>();

            return await QueryOverride<T>(databaseName, schemaName);
        }

        /// <summary>
        /// Query the database for a given type. Automatically opens the database if needed.
        /// </summary>
        public async ValueTask<IMagicQuery<T>> Query<T>() 
            where T : class, IMagicTableBase, new()
        {            
            string databaseName = SchemaHelper.GetDefaultDatabaseName<T>();
            string schemaName = SchemaHelper.GetTableName<T>();
            var dbManager = await GetOrCreateDatabaseAsync();
            return await QueryOverride<T>(databaseName, schemaName);
        }

        [Obsolete("Not decided if this will be built in further or removed")]
        public async ValueTask<IMagicQuery<T>> QueryOverride<T>(string databaseNameOverride, string schemaNameOverride) 
            where T: class, IMagicTableBase, new ()
        {
            var dbManager = await GetOrCreateDatabaseAsync(); // Ensure database is open
            return dbManager.Query<T>(databaseNameOverride, schemaNameOverride);
        }
        
        public async ValueTask<IMagicDatabaseScoped> Database(IndexedDbSet indexedDbSet)
        {
            var dbManager = await GetOrCreateDatabaseAsync(); // Ensure database is open
            return dbManager.Database(dbManager, indexedDbSet);
        }

        //public async ValueTask<IMagicDatabaseScoped> Database()
        //{
        //    throw new Exception("Still working on it.");
        //}

    }
}