using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Magic.IndexedDb.Factories;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;
using Microsoft.JSInterop;
using System.Text.Json.Nodes;
using Magic.IndexedDb.Interfaces;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Microsoft.Extensions.Options;
using Magic.IndexedDb.Extensions;
using System.Runtime.CompilerServices;
using Magic.IndexedDb.Models.UniversalOperations;

namespace Magic.IndexedDb
{
    /// <summary>
    /// Provides functionality for accessing IndexedDB from Blazor application
    /// </summary>
    public sealed class IndexedDbManager : IMagicManager
    {
        internal static async ValueTask<IndexedDbManager> CreateAndOpenAsync(
            CancellationToken cancellationToken = default)
        {
            var result = new IndexedDbManager(dbStore, jsRuntime);
            await result.CallJsAsync(Cache.MagicDbJsImportPath, 
                IndexedDbFunctions.CREATE_DB, cancellationToken, new TypedArgument<DbStore>(dbStore));
            return result;
        }

        readonly DbStore _dbStore;
        readonly IJSObjectReference _jsModule;

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="dbStore"></param>
        /// <param name="jsRuntime"></param>
        private IndexedDbManager(DbStore dbStore, IJSObjectReference jsRuntime)
        {
            this._dbStore = dbStore;
            this._jsModule = jsRuntime;
        }

        // TODO: make it readonly
        public List<StoreSchema> Stores => this._dbStore.StoreSchemas;
        public int CurrentVersion => _dbStore.Version;
        //public string DbName => _dbStore.Name;

        /// <summary>
        /// Deletes the database corresponding to the dbName passed in
        /// </summary>
        /// <param name="dbName">The name of database to delete</param>
        /// <returns></returns>
        public Task DeleteDbAsync(string dbName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(dbName))
            {
                throw new ArgumentException("dbName cannot be null or empty", nameof(dbName));
            }
            return CallJsAsync(Cache.MagicDbJsImportPath, IndexedDbFunctions.DELETE_DB, cancellationToken, new TypedArgument<string>(dbName));
        }

        //public async Task AddAsync<T>(T record, CancellationToken cancellationToken = default) where T : class
        //{
        //    _ = await AddAsync<T, JsonElement>(record, cancellationToken);
        //}

        internal async Task<TKey> AddAsync<T, TKey>(T record, string dbName, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            StoreRecord<T?> RecordToSend = new StoreRecord<T?>()
            {
                DbName = dbName,
                StoreName = schemaName,
                Record = record
            };
            return await CallJsAsync<TKey>(Cache.MagicDbJsImportPath, IndexedDbFunctions.ADD_ITEM, cancellationToken, new TypedArgument<StoreRecord<T?>>(RecordToSend));
        }

        /// <summary>
        /// Adds records/objects to the specified store in bulk
        /// Waits for response
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="recordsToBulkAdd">An instance of StoreRecord that provides the store name and the data to add</param>
        /// <returns></returns>
        internal Task BulkAddRecordAsync<T>(
            string storeName, string dbName,
            IEnumerable<T> recordsToBulkAdd,
            CancellationToken cancellationToken = default)
        {
            // TODO: https://github.com/magiccodingman/Magic.IndexedDb/issues/9

            return CallJsAsync(Cache.MagicDbJsImportPath, IndexedDbFunctions.BULKADD_ITEM, cancellationToken,
                new ITypedArgument[] { new TypedArgument<string>(dbName),
                    new TypedArgument<string>(storeName),
                    new TypedArgument<IEnumerable<T>>(recordsToBulkAdd) });
        }

        

        internal async Task<int> UpdateAsync<T>(T item, string dbName, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            object? primaryKeyValue = AttributeHelpers.GetPrimaryKeyValue<T>(item);
            if (primaryKeyValue is null)
                throw new ArgumentException("Item being updated must have a key.");

            UpdateRecord<T> record = new UpdateRecord<T>()
            {
                Key = primaryKeyValue,
                DbName = dbName,
                StoreName = schemaName,
                Record = item
            };

            return await CallJsAsync<int>(Cache.MagicDbJsImportPath, 
                IndexedDbFunctions.UPDATE_ITEM, cancellationToken, new TypedArgument<UpdateRecord<T?>>(record));
        }

        internal async Task<int> UpdateRangeAsync<T>(
    IEnumerable<T> items, string dbName,
    CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            var recordsToUpdate = items.Select(item =>
            {
                object? primaryKeyValue = AttributeHelpers.GetPrimaryKeyValue<T>(item);
                if (primaryKeyValue is null)
                    throw new ArgumentException("Item being updated must have a key.");

                return new UpdateRecord<T>()
                {
                    Key = primaryKeyValue,
                    DbName = dbName,
                    StoreName = schemaName,
                    Record = item
                };
            });

            return await CallJsAsync<int>(Cache.MagicDbJsImportPath,
                IndexedDbFunctions.BULKADD_UPDATE, cancellationToken, new TypedArgument<IEnumerable<UpdateRecord<T>>>(recordsToUpdate));
        }


        // No longer supported. Instead use query.Where(x => x.Id == 3).FirstOrDefault()
        /*public async Task<T?> GetByIdAsync<T>(
            object key,
            CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            // Validate key type
            AttributeHelpers.ValidatePrimaryKey<T>(key);

            return await CallJsAsync<T>(Cache.MagicDbJsImportPath,
                IndexedDbFunctions.FIND_ITEM, cancellationToken,
                new ITypedArgument[] { new TypedArgument<string>(DbName), new TypedArgument<string>(schemaName), new TypedArgument<object>(key) });
        }*/

        public IMagicQuery<T> Query<T>() where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            MagicQuery<T> query = new MagicQuery<T>(schemaName, this);
            return query;
        }

        /// <summary>
        /// Results do not come back in the same order.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="storeName"></param>
        /// <param name="jsonQuery"></param>
        /// <param name="query"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        internal async Task<IEnumerable<T>?> LinqToIndedDb<T>(
            string storeName, NestedOrFilter nestedOrFilter, MagicQuery<T> query,
            CancellationToken cancellationToken) where T : class
        {
            if (nestedOrFilter.universalFalse == true)
                return default;

            string databaseName = SchemaHelper.GetDatabaseName<T>();

            var args = new ITypedArgument[] {
                new TypedArgument<string>(databaseName),
                new TypedArgument<string>(storeName),
                new TypedArgument<NestedOrFilter>(nestedOrFilter),
                new TypedArgument<List<StoredMagicQuery>?>(query?.StoredMagicQueries),
                new TypedArgument<bool>(query?.ForceCursorMode??false),
            };

            return await CallJsAsync<IEnumerable<T>>
                (Cache.MagicDbJsImportPath, IndexedDbFunctions.WHERE, cancellationToken,
                args);
        }

        /// <summary>
        /// Applying the returned ordering on this isn't possible to be guarenteed. 
        /// Developer must be informed that ordering is correct in the IndexDB response 
        /// but that the returned values may not be in the same order, so they must 
        /// manually re-apply the ordering desired.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="storeName"></param>
        /// <param name="jsonQuery"></param>
        /// <param name="query"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        internal async IAsyncEnumerable<T?> LinqToIndedDbYield<T>(
    string storeName, NestedOrFilter nestedOrFilter, MagicQuery<T> query,
    [EnumeratorCancellation] CancellationToken cancellationToken) where T : class
        {
            if (nestedOrFilter.universalFalse == true)
                yield break; // Terminate the async iterator immediately.

            string databaseName = SchemaHelper.GetDatabaseName<T>();

            var args = new ITypedArgument[] {
        new TypedArgument<string>(databaseName),
        new TypedArgument<string>(storeName),
        new TypedArgument<NestedOrFilter>(nestedOrFilter),
        new TypedArgument<List<StoredMagicQuery>?>(query?.StoredMagicQueries),
        new TypedArgument<bool>(query?.ForceCursorMode??false),
    };

            // Yield results **as they arrive** from JS
            await foreach (var item in CallYieldJsAsync<T>(Cache.MagicDbJsImportPath, IndexedDbFunctions.WHERE_YIELD, cancellationToken, args))
            {
                yield return item; // Stream each item immediately
            }
        }        

        /// <summary>
        /// Returns Mb
        /// </summary>
        /// <returns></returns>
        public Task<QuotaUsage> GetStorageEstimateAsync(CancellationToken cancellationToken = default)
        {
            return CallJsAsync<QuotaUsage>(Cache.MagicDbJsImportPath, IndexedDbFunctions.GET_STORAGE_ESTIMATE, cancellationToken, []);
        }

        // No longer supported in current LINQ system. Use Query.ToListAsync() for equivilent.
        /*public async Task<IEnumerable<T>> GetAllAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            return await CallJsAsync<IEnumerable<T>>(Cache.MagicDbJsImportPath,
                IndexedDbFunctions.TOARRAY, cancellationToken,
                new ITypedArgument[] { new TypedArgument<string>(DbName), new TypedArgument<string>(schemaName) });
        }*/

        internal async Task DeleteAsync<T>(T item, string dbName, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            object? primaryKeyValue = AttributeHelpers.GetPrimaryKeyValue<T>(item);

            UpdateRecord<T> record = new UpdateRecord<T>()
            {
                Key = primaryKeyValue,
                DbName = dbName,
                StoreName = schemaName,
                Record = item
            };

            await CallJsAsync(Cache.MagicDbJsImportPath, IndexedDbFunctions.DELETE_ITEM, cancellationToken, new TypedArgument<UpdateRecord<T?>>(record));
        }

        internal async Task<int> DeleteRangeAsync<T>(
    IEnumerable<T> items, string dbName, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            var keys = items.Select(item =>
            {
                object? primaryKeyValue = AttributeHelpers.GetPrimaryKeyValue(item);
                if (primaryKeyValue is null)
                    throw new ArgumentException("Item being deleted must have a key.");
                return primaryKeyValue;
            });

            var args = new ITypedArgument[] {
                new TypedArgument<string>(dbName),
                new TypedArgument<string>(schemaName),
                new TypedArgument<IEnumerable<object>?>(keys) };

            return await CallJsAsync<int>(Cache.MagicDbJsImportPath,
                IndexedDbFunctions.BULK_DELETE, cancellationToken,
                args);
        }


        /// <summary>
        /// Clears all data from a Table but keeps the table
        /// Wait for response
        /// </summary>
        /// <param name="storeName"></param>
        /// <returns></returns>
        public Task ClearTableAsync(string storeName, string dbName, CancellationToken cancellationToken = default)
        {
            return CallJsAsync(Cache.MagicDbJsImportPath, IndexedDbFunctions.CLEAR_TABLE, cancellationToken,
                new ITypedArgument[] { new TypedArgument<string>(dbName), new TypedArgument<string>(storeName) });
        }

        /// <summary>
        /// Clears all data from a Table but keeps the table
        /// Wait for response
        /// </summary>
        /// <returns></returns>
        public Task ClearTableAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            string databaseName = SchemaHelper.GetDatabaseName<T>();
            return ClearTableAsync(schemaName, databaseName, cancellationToken);
        }

        internal async Task CallJsAsync(string modulePath, string functionName, 
            CancellationToken token, params ITypedArgument[] args)
        {
            var magicJsInvoke = new MagicJsInvoke(_jsModule);

            await magicJsInvoke.MagicVoidStreamJsAsync(modulePath, functionName, token, args);
        }

        internal async Task<T> CallJsAsync<T>(string modulePath, string functionName, 
            CancellationToken token, params ITypedArgument[] args)
        {

            var magicJsInvoke = new MagicJsInvoke(_jsModule);

            return await magicJsInvoke.MagicStreamJsAsync<T>(modulePath, functionName, token, args) ?? default;
        }

        internal async IAsyncEnumerable<T?> CallYieldJsAsync<T>(
    string modulePath,
    string functionName,
    [EnumeratorCancellation] CancellationToken token,
    params ITypedArgument[] args)
        {
            var magicJsInvoke = new MagicJsInvoke(_jsModule);

            await foreach (var item in magicJsInvoke.MagicYieldJsAsync<T>(modulePath, functionName, token, args)
                .WithCancellation(token)) // Ensure cancellation works in the async stream
            {
                yield return item; // Yield items as they arrive
            }
        }


    }
}