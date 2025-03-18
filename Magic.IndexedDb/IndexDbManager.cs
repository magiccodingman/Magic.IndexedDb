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
    internal class IndexedDbManager
    {
        private readonly IJSObjectReference _jsModule;  // Shared JS module

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="dbStore"></param>
        /// <param name="jsRuntime"></param>
        internal IndexedDbManager(IJSObjectReference jsModule)
        {
            _jsModule = jsModule; // Use shared JS module reference
        }

        //public string DbName => _dbStore.Name;

        /// <summary>
        /// Deletes the database corresponding to the dbName passed in
        /// </summary>
        /// <param name="dbName">The name of database to delete</param>
        /// <returns></returns>
        internal Task DeleteDbAsync(string dbName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(dbName))
            {
                throw new ArgumentException("dbName cannot be null or empty", nameof(dbName));
            }
            return new MagicJsInvoke(_jsModule).CallJsAsync(Cache.MagicDbJsImportPath, IndexedDbFunctions.DELETE_DB, cancellationToken, new TypedArgument<string>(dbName));
        }

        internal async Task<TKey> AddAsync<T, TKey>(T record, string dbName, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetTableName<T>();

            StoreRecord<T?> RecordToSend = new StoreRecord<T?>()
            {
                DbName = dbName,
                StoreName = schemaName,
                Record = record
            };
            return await new MagicJsInvoke(_jsModule).CallJsAsync<TKey>(Cache.MagicDbJsImportPath, 
                IndexedDbFunctions.ADD_ITEM, cancellationToken, new TypedArgument<StoreRecord<T?>>(RecordToSend))
                ?? throw new Exception($"An Error occurred trying to add your record of type: {typeof(T).Name}");
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

            return new MagicJsInvoke(_jsModule).CallJsAsync(Cache.MagicDbJsImportPath, IndexedDbFunctions.BULKADD_ITEM, cancellationToken,
                new ITypedArgument[] { new TypedArgument<string>(dbName),
                    new TypedArgument<string>(storeName),
                    new TypedArgument<IEnumerable<T>>(recordsToBulkAdd) });
        }



        internal async Task<int> UpdateAsync<T>(T item, string dbName, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetTableName<T>();

            List<PrimaryKeys> primaryKeyValue = AttributeHelpers.GetPrimaryKeys<T>(item);
            if (primaryKeyValue is null)
                throw new ArgumentException("Item being updated must have a key.");

            UpdateRecord<T> record = new UpdateRecord<T>()
            {
                Key = primaryKeyValue,
                DbName = dbName,
                StoreName = schemaName,
                Record = item
            };

            return await new MagicJsInvoke(_jsModule).CallJsAsync<int>(Cache.MagicDbJsImportPath,
                IndexedDbFunctions.UPDATE_ITEM, cancellationToken, new TypedArgument<UpdateRecord<T?>>(record));
        }

        internal async Task<int> CountEntireTableAsync<T>(string schemaName, string dbName)
        {
            return await new MagicJsInvoke(_jsModule).CallInvokeDefaultJsAsync<int>(Cache.MagicDbJsImportPath,
                IndexedDbFunctions.COUNT_TABLE,
                dbName, schemaName
                );
        }

        internal async Task<int> UpdateRangeAsync<T>(
    IEnumerable<T> items, string dbName,
    CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetTableName<T>(); 

            var recordsToUpdate = items.Select(item =>
            {
                List<PrimaryKeys> primaryKeyValue = AttributeHelpers.GetPrimaryKeys<T>(item);
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

            return await new MagicJsInvoke(_jsModule).CallJsAsync<int>(Cache.MagicDbJsImportPath,
                IndexedDbFunctions.BULKADD_UPDATE, cancellationToken, new TypedArgument<IEnumerable<UpdateRecord<T>>>(recordsToUpdate));
        }

        internal IMagicQuery<T> Query<T>(string databaseName, string schemaName) where T : class
        {
            MagicQuery<T> query = new MagicQuery<T>(databaseName, schemaName, this);
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
            NestedOrFilter nestedOrFilter, MagicQuery<T> query,
            CancellationToken cancellationToken) where T : class
        {
            if (nestedOrFilter.universalFalse == true)
                return default;
            var args = new ITypedArgument[] {
                new TypedArgument<string>(query.DatabaseName),
                new TypedArgument<string>(query.SchemaName),
                new TypedArgument<NestedOrFilter>(nestedOrFilter),
                new TypedArgument<List<StoredMagicQuery>?>(query?.StoredMagicQueries),
                new TypedArgument<bool>(query?.ForceCursorMode??false),
            };

            return await new MagicJsInvoke(_jsModule).CallJsAsync<IEnumerable<T>>
                (Cache.MagicDbJsImportPath, IndexedDbFunctions.MAGIC_QUERY_ASYNC, cancellationToken,
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
    NestedOrFilter nestedOrFilter, MagicQuery<T> query,
    [EnumeratorCancellation] CancellationToken cancellationToken) where T : class
        {
            if (nestedOrFilter.universalFalse == true)
                yield break; // Terminate the async iterator immediately.

            var args = new ITypedArgument[] {
        new TypedArgument<string>(query.DatabaseName),
        new TypedArgument<string>(query.SchemaName),
        new TypedArgument<NestedOrFilter>(nestedOrFilter),
        new TypedArgument<List<StoredMagicQuery>?>(query?.StoredMagicQueries),
        new TypedArgument<bool>(query?.ForceCursorMode??false),
    };

            // Yield results **as they arrive** from JS
            await foreach (var item in new MagicJsInvoke(_jsModule).CallYieldJsAsync<T>(Cache.MagicDbJsImportPath, IndexedDbFunctions.MAGIC_QUERY_YIELD, cancellationToken, args))
            {
                yield return item; // Stream each item immediately
            }
        }

        internal async Task DeleteAsync<T>(T item, string dbName, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetTableName<T>();

            List<PrimaryKeys> primaryKeyValue = AttributeHelpers.GetPrimaryKeys<T>(item);

            UpdateRecord<T> record = new UpdateRecord<T>()
            {
                Key = primaryKeyValue,
                DbName = dbName,
                StoreName = schemaName,
                Record = item
            };

            await new MagicJsInvoke(_jsModule).CallJsAsync(Cache.MagicDbJsImportPath, IndexedDbFunctions.DELETE_ITEM, cancellationToken, new TypedArgument<UpdateRecord<T?>>(record));
        }

        internal async Task<int> DeleteRangeAsync<T>(
    IEnumerable<T> items, string dbName, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetTableName<T>();

            var keys = items.Select(item =>
            {
                List<PrimaryKeys> primaryKeyValue = AttributeHelpers.GetPrimaryKeys<T>(item);
                if (primaryKeyValue is null)
                    throw new ArgumentException("Item being deleted must have a key.");
                return primaryKeyValue;
            });

            var args = new ITypedArgument[] {
                new TypedArgument<string>(dbName),
                new TypedArgument<string>(schemaName),
                new TypedArgument<IEnumerable<object>?>(keys) };

            return await new MagicJsInvoke(_jsModule).CallJsAsync<int>(Cache.MagicDbJsImportPath,
                IndexedDbFunctions.BULK_DELETE, cancellationToken,
                args);
        }


        /// <summary>
        /// Clears all data from a Table but keeps the table
        /// Wait for response
        /// </summary>
        /// <param name="storeName"></param>
        /// <returns></returns>
        internal Task ClearTableAsync(string storeName, string dbName)
        {
            return new MagicJsInvoke(_jsModule).CallInvokeVoidDefaultJsAsync(Cache.MagicDbJsImportPath, 
                IndexedDbFunctions.CLEAR_TABLE, dbName, storeName);
        }


        /// <summary>
        /// Clears all data from a Table but keeps the table
        /// Wait for response
        /// </summary>
        /// <returns></returns>
       /* internal Task ClearTableAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetTableName<T>();
            string databaseName = SchemaHelper.GetDefaultDatabaseName<T>();
            return ClearTableAsync(schemaName, databaseName, cancellationToken);
        }*/


    }
}