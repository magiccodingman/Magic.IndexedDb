using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;
using Microsoft.JSInterop;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Extensions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Magic.IndexedDb.LinqTranslation.Interfaces;
using Magic.IndexedDb.LinqTranslation.Models;

namespace Magic.IndexedDb;

/// <summary>
/// Provides functionality for accessing IndexedDB from Blazor application
/// </summary>
internal class IndexedDbManager
{
    private readonly IJSObjectReference _jsModule;  // Shared JS module
    private readonly long _jsMessageSizeBytes;
    private readonly IReadOnlyDictionary<string, string>? _protectedRoutes;
    private readonly IReadOnlyDictionary<string, MagicJsonSerializationSettings> _numericSettings;
    private readonly MagicJsonSerializationSettings _legacySettings = new() { UseCamelCase = true };

    private readonly CancellationToken _lifetime;

    private string Route(string database)
    {
        _lifetime.ThrowIfCancellationRequested();
        return _protectedRoutes?.GetValueOrDefault(database) ?? database;
    }

    internal MagicJsonSerializationSettings Settings(string database)
    {
        _lifetime.ThrowIfCancellationRequested();
        return _numericSettings.GetValueOrDefault(database) ?? _legacySettings;
    }

    private MagicJsInvoke Invocation(string database) => new(_jsModule, _jsMessageSizeBytes, Settings(database));

    private List<PrimaryKeys> RecordKeys<T>(T record, string database) where T : class
    {
        var keys = AttributeHelpers.GetPrimaryKeys(record);
        var settings = Settings(database);
        if (settings.NumericStorageMode == MagicNumericStorageMode.Legacy) return keys;

        var properties = PropertyMappingCache.GetTypeOfTProperties(typeof(T));
        var options = settings.GetOptionsWithNumericConverters();
        foreach (var key in keys)
        {
            var property = properties.GetPropertyByCsharpName(properties.GetCsharpPropertyName(key.JsName)).Property;
            if (MagicNumericSerialization.IsNativeAutoIncrementKey(property, typeof(T)))
            {
                if (MagicConverterHelper.HasCustomConverter(property, options))
                    throw new InvalidOperationException("A lossless native auto-increment key cannot use a custom JSON converter.");
                key.Value = MagicNumericSerialization.SerializeNativeKey(key.Value, property.PropertyType);
            }
            else
            {
                key.Value = MagicConverterHelper.SerializePropertyValue(property, key.Value, options);
            }
        }
        return keys;
    }

    /// <summary>
    /// Ctor
    /// </summary>
    /// <param name="dbStore"></param>
    /// <param name="jsRuntime"></param>
    internal IndexedDbManager(IJSObjectReference jsModule, long jsMessageSizeBytes,
        IReadOnlyDictionary<string, string>? protectedRoutes = null, CancellationToken lifetime = default,
        IReadOnlyDictionary<string, MagicNumericStorageMode>? numericModes = null)
    {
        _jsModule = jsModule; // Use shared JS module reference
        _jsMessageSizeBytes = jsMessageSizeBytes;
        _protectedRoutes = protectedRoutes;
        _lifetime = lifetime;
        _numericSettings = numericModes?.ToDictionary(pair => pair.Key, pair =>
            new MagicJsonSerializationSettings { UseCamelCase = true, NumericStorageMode = pair.Value }, StringComparer.Ordinal)
            ?? new Dictionary<string, MagicJsonSerializationSettings>(StringComparer.Ordinal);
    }

    //public string DbName => _dbStore.Name;

    /// <summary>
    /// Deletes the database corresponding to the dbName passed in
    /// </summary>
    /// <param name="dbName">The name of database to delete</param>
    /// <returns></returns>
    internal Task DeleteDbAsync(string dbName)
    {
        if (string.IsNullOrEmpty(dbName))
        {
            throw new ArgumentException("dbName cannot be null or empty", nameof(dbName));
        }
        return Invocation(dbName).CallInvokeVoidDefaultJsAsync(Cache.MagicDbJsImportPath,
            IndexedDbFunctions.DELETE_DB, Route(dbName));
    }

    internal Task CloseDbAsync(string dbName)
    {
        if (string.IsNullOrEmpty(dbName))
        {
            throw new ArgumentException("dbName cannot be null or empty", nameof(dbName));
        }
        return Invocation(dbName).CallInvokeVoidDefaultJsAsync(Cache.MagicDbJsImportPath,
            IndexedDbFunctions.CLOSE_DB, Route(dbName));
    }

    internal Task OpenDbAsync(string dbName)
    {
        if (string.IsNullOrEmpty(dbName))
        {
            throw new ArgumentException("dbName cannot be null or empty", nameof(dbName));
        }
        return Invocation(dbName).CallInvokeVoidDefaultJsAsync(Cache.MagicDbJsImportPath,
            IndexedDbFunctions.OPEN_DB, Route(dbName));
    }

    internal Task<bool> DoesDbExist(string dbName)
    {
        if (string.IsNullOrEmpty(dbName))
        {
            throw new ArgumentException("dbName cannot be null or empty", nameof(dbName));
        }
        return Invocation(dbName).CallInvokeDefaultJsAsync<bool>(Cache.MagicDbJsImportPath,
            IndexedDbFunctions.DOES_DB_EXIST, Route(dbName));
    }

    internal Task<bool> IsDbOpen(string dbName)
    {
        if (string.IsNullOrEmpty(dbName))
        {
            throw new ArgumentException("dbName cannot be null or empty", nameof(dbName));
        }
        return Invocation(dbName).CallInvokeDefaultJsAsync<bool>(Cache.MagicDbJsImportPath,
            IndexedDbFunctions.IS_DB_CLosed, Route(dbName));
    }

    internal async Task<TKey> AddAsync<T, TKey>(T record, string dbName, CancellationToken cancellationToken = default) where T : class
    {
        string schemaName = SchemaHelper.GetTableName<T>();

        StoreRecord<T?> RecordToSend = new StoreRecord<T?>()
        {
            DbName = Route(dbName),
            StoreName = schemaName,
            Record = record
        };
        var settings = Settings(dbName);
        var responseSettings = settings.NumericStorageMode == MagicNumericStorageMode.Lossless &&
            SchemaHelper.GetStoreSchema(typeof(T)).PrimaryKeyAuto ? _legacySettings : null;
        return await new MagicJsInvoke(_jsModule, _jsMessageSizeBytes, settings, responseSettings).CallJsAsync<TKey>(Cache.MagicDbJsImportPath,
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

        return Invocation(dbName).CallJsAsync(Cache.MagicDbJsImportPath, IndexedDbFunctions.BULKADD_ITEM, cancellationToken,
            new ITypedArgument[] { new TypedArgument<string>(Route(dbName)),
                new TypedArgument<string>(storeName),
                new TypedArgument<IEnumerable<T>>(recordsToBulkAdd) });
    }



    internal async Task<int> UpdateAsync<T>(T item, string dbName, CancellationToken cancellationToken = default) where T : class
    {
        string schemaName = SchemaHelper.GetTableName<T>();

        List<PrimaryKeys> primaryKeyValue = RecordKeys(item, dbName);
        if (primaryKeyValue is null)
            throw new ArgumentException("Item being updated must have a key.");

        UpdateRecord<T> record = new UpdateRecord<T>()
        {
            Key = primaryKeyValue,
            DbName = Route(dbName),
            StoreName = schemaName,
            Record = item
        };

        return await Invocation(dbName).CallJsAsync<int>(Cache.MagicDbJsImportPath,
            IndexedDbFunctions.UPDATE_ITEM, cancellationToken, new TypedArgument<UpdateRecord<T?>>(record));
    }

    internal async Task<int> CountEntireTableAsync<T>(string schemaName, string dbName)
    {
        return await Invocation(dbName).CallInvokeDefaultJsAsync<int>(Cache.MagicDbJsImportPath,
            IndexedDbFunctions.COUNT_TABLE,
            Route(dbName), schemaName
        );
    }

    internal async Task<int> UpdateRangeAsync<T>(
        IEnumerable<T> items, string dbName,
        CancellationToken cancellationToken = default) where T : class
    {
        string schemaName = SchemaHelper.GetTableName<T>(); 

        var recordsToUpdate = items.Select(item =>
        {
            List<PrimaryKeys> primaryKeyValue = RecordKeys(item, dbName);
            if (primaryKeyValue is null)
                throw new ArgumentException("Item being updated must have a key.");

            return new UpdateRecord<T>()
            {
                Key = primaryKeyValue,
                DbName = Route(dbName),
                StoreName = schemaName,
                Record = item
            };
        });

        return await Invocation(dbName).CallJsAsync<int>(Cache.MagicDbJsImportPath,
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
    internal async Task<IEnumerable<T>?> LinqToIndexedDb<T>(
        FilterNode nestedOrFilter, MagicQuery<T> query,
        CancellationToken cancellationToken) where T : class
    {
        //if (nestedOrFilter.universalFalse == true)
        //    return default;

        var args = new ITypedArgument[] {
            new TypedArgument<string>(Route(query.DatabaseName)),
            new TypedArgument<string>(query.SchemaName),
            new TypedArgument<FilterNode>(nestedOrFilter),
            new TypedArgument<List<StoredMagicQuery>?>(query?.StoredMagicQueries),
            new TypedArgument<bool>(query?.ForceCursorMode??false),
        };

        var rows = await Invocation(query.DatabaseName).CallJsAsync<IEnumerable<T>>(
            Cache.MagicDbJsImportPath, IndexedDbFunctions.MAGIC_QUERY_ASYNC, cancellationToken, args);
        _lifetime.ThrowIfCancellationRequested();
        return rows;
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
    internal async IAsyncEnumerable<T?> LinqToIndexedDbYield<T>(
        FilterNode nestedOrFilter, MagicQuery<T> query,
        [EnumeratorCancellation] CancellationToken cancellationToken) where T : class
    {

        var args = new ITypedArgument[] {
            new TypedArgument<string>(Route(query.DatabaseName)),
            new TypedArgument<string>(query.SchemaName),
            new TypedArgument<FilterNode>(nestedOrFilter),
            new TypedArgument<List<StoredMagicQuery>?>(query?.StoredMagicQueries),
            new TypedArgument<bool>(query?.ForceCursorMode??false),
        };

        // Yield results **as they arrive** from JS
        await foreach (var item in Invocation(query.DatabaseName).CallYieldJsAsync<T>(Cache.MagicDbJsImportPath, IndexedDbFunctions.MAGIC_QUERY_YIELD, cancellationToken, args))
        {
            _lifetime.ThrowIfCancellationRequested();
            yield return item; // Stream each item immediately
        }
    }

    internal async Task<JsonElement[]> ProjectAsync<T>(MagicQuery<T> query, string[] fields,
        CancellationToken token) where T : class
    {
        var rows = await Invocation(query.DatabaseName).CallJsAsync<JsonElement[]>(
            Cache.MagicDbJsImportPath, "projectRecords", token, ProjectionArguments(query, fields));
        _lifetime.ThrowIfCancellationRequested();
        return rows ?? [];
    }

    internal async IAsyncEnumerable<JsonElement> ProjectStreamAsync<T>(MagicQuery<T> query, string[] fields,
        [EnumeratorCancellation] CancellationToken token) where T : class
    {
        await foreach (var row in Invocation(query.DatabaseName).CallYieldJsAsync<JsonElement>(
            Cache.MagicDbJsImportPath, "projectRecordStream", token, ProjectionArguments(query, fields)))
        {
            _lifetime.ThrowIfCancellationRequested();
            yield return row;
        }
    }

    private ITypedArgument[] ProjectionArguments<T>(MagicQuery<T> query, string[] fields) where T : class =>
    [
        new TypedArgument<string>(Route(query.DatabaseName)),
        new TypedArgument<string>(query.SchemaName),
        new TypedArgument<FilterNode>(new LinqTranslation.Extensions.MagicQueryExtensions<T>(query).GetCollectedBinaryJsonExpressions()),
        new TypedArgument<List<StoredMagicQuery>>(query.StoredMagicQueries),
        new TypedArgument<bool>(query.ForceCursorMode),
        new TypedArgument<string[]>(fields)
    ];

    internal async Task<JsonElement[]> ProjectPublicAsync<T>(MagicQuery<T> query, FilterNode predicate,
        string member, CancellationToken token) where T : class
    {
        var args = new ITypedArgument[]
        {
            new TypedArgument<string>(Route(query.DatabaseName)),
            new TypedArgument<string>(query.SchemaName),
            new TypedArgument<FilterNode>(predicate),
            new TypedArgument<List<StoredMagicQuery>>(query.StoredMagicQueries),
            new TypedArgument<bool>(query.ForceCursorMode),
            new TypedArgument<string[]>([member])
        };
        var rows = await Invocation(query.DatabaseName).CallJsAsync<JsonElement[]>(
            Cache.MagicDbJsImportPath, "projectPublicRecords", token, args);
        _lifetime.ThrowIfCancellationRequested();
        return rows ?? [];
    }

    internal Task<int> PatchPublicAsync<T>(MagicQuery<T> query, object key, JsonElement changes,
        CancellationToken token) where T : class =>
        Invocation(query.DatabaseName).CallJsAsync<int>(
            Cache.MagicDbJsImportPath, "patchPublicRecord", token,
            new TypedArgument<string>(Route(query.DatabaseName)),
            new TypedArgument<string>(query.SchemaName),
            new TypedArgument<JsonElement>(JsonSerializer.SerializeToElement(key)),
            new TypedArgument<JsonElement>(changes));

    internal async Task DeleteAsync<T>(T item, string dbName, CancellationToken cancellationToken = default) where T : class
    {
        string schemaName = SchemaHelper.GetTableName<T>();

        List<PrimaryKeys> primaryKeyValue = RecordKeys(item, dbName);

        UpdateRecord<T> record = new UpdateRecord<T>()
        {
            Key = primaryKeyValue,
            DbName = Route(dbName),
            StoreName = schemaName,
            Record = item
        };

        await Invocation(dbName).CallJsAsync(Cache.MagicDbJsImportPath, IndexedDbFunctions.DELETE_ITEM, cancellationToken, new TypedArgument<UpdateRecord<T?>>(record));
    }

    internal async Task<int> DeleteRangeAsync<T>(
        IEnumerable<T> items, string dbName, CancellationToken cancellationToken = default) where T : class
    {
        string schemaName = SchemaHelper.GetTableName<T>();

        var keys = items.Select(item =>
        {
            List<PrimaryKeys> primaryKeyValue = RecordKeys(item, dbName);
            if (primaryKeyValue is null)
                throw new ArgumentException("Item being deleted must have a key.");
            return primaryKeyValue;
        });

        var args = new ITypedArgument[] {
            new TypedArgument<string>(Route(dbName)),
            new TypedArgument<string>(schemaName),
            new TypedArgument<IEnumerable<object>?>(keys) };

        return await Invocation(dbName).CallJsAsync<int>(Cache.MagicDbJsImportPath,
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
        return Invocation(dbName).CallInvokeVoidDefaultJsAsync(Cache.MagicDbJsImportPath,
            IndexedDbFunctions.CLEAR_TABLE, Route(dbName), storeName);
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
