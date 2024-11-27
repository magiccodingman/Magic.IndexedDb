using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using static System.Collections.Specialized.BitVector32;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Magic.IndexedDb
{
    /// <summary>
    /// Provides functionality for accessing IndexedDB from Blazor application
    /// </summary>
    public sealed class IndexedDbManager : IAsyncDisposable
    {
        readonly DbStore _dbStore;
        readonly IJSRuntime _jsRuntime;
        readonly DotNetObjectReference<IndexedDbManager> _objReference;
        public Task<IJSObjectReference> JsModule { get; }

        public async ValueTask DisposeAsync()
        {
            _objReference.Dispose();

            var module = await JsModule;
            await module.DisposeAsync();
        }

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="dbStore"></param>
        /// <param name="jsRuntime"></param>
        internal IndexedDbManager(DbStore dbStore, IJSRuntime jsRuntime)
        {
            _objReference = DotNetObjectReference.Create(this);
            _dbStore = dbStore;
            _jsRuntime = jsRuntime;
            this.JsModule = jsRuntime.InvokeAsync<IJSObjectReference>(
                "import", 
                "./_content/Magic.IndexedDb/magicDB.js").AsTask();
        }

        public List<StoreSchema> Stores => _dbStore.StoreSchemas;
        public string CurrentVersion => _dbStore.Version;
        public string DbName => _dbStore.Name;

        /// <summary>
        /// Opens the IndexedDB defined in the DbStore. Under the covers will create the database if it does not exist
        /// and create the stores defined in DbStore.
        /// </summary>
        /// <returns></returns>
        public Task OpenDbAsync(CancellationToken cancellationToken = default)
        {
            return CallJs(IndexedDbFunctions.CREATE_DB, cancellationToken, [_dbStore]);
        }

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
            return CallJs(IndexedDbFunctions.DELETE_DB, cancellationToken, [dbName]);
        }

        public async Task AddAsync<T>(T record, CancellationToken cancellationToken = default) where T : class
        {
            // TODO: https://github.com/magiccodingman/Magic.IndexedDb/issues/9

            string schemaName = SchemaHelper.GetSchemaName<T>();

            T? myClass = null;
            object? processedRecord = await ProcessRecord(record);
            if (processedRecord is ExpandoObject)
                myClass = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(processedRecord));
            else
                myClass = (T?)processedRecord;

            Dictionary<string, object?>? convertedRecord = null;
            if (processedRecord is ExpandoObject)
            {
                var result = ((ExpandoObject)processedRecord)?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
                if (result != null)
                {
                    convertedRecord = result;
                }
            }
            else
            {
                convertedRecord = ManagerHelper.ConvertRecordToDictionary(myClass);
            }
            var propertyMappings = ManagerHelper.GeneratePropertyMapping<T>();

            // Convert the property names in the convertedRecord dictionary
            if (convertedRecord != null)
            {
                var updatedRecord = ManagerHelper.ConvertPropertyNamesUsingMappings(convertedRecord, propertyMappings);

                if (updatedRecord != null)
                {
                    StoreRecord<Dictionary<string, object?>> RecordToSend = new StoreRecord<Dictionary<string, object?>>()
                    {
                        DbName = this.DbName,
                        StoreName = schemaName,
                        Record = updatedRecord
                    };

                    await CallJs(IndexedDbFunctions.ADD_ITEM, cancellationToken, [RecordToSend]);
                }
            }
        }

        public async Task<string> Decrypt(string EncryptedValue)
        {
            EncryptionFactory encryptionFactory = new EncryptionFactory(_jsRuntime, this);
            string decryptedValue = await encryptionFactory.Decrypt(EncryptedValue, _dbStore.EncryptionKey);
            return decryptedValue;
        }

        private async Task<object?> ProcessRecord<T>(T record) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            StoreSchema? storeSchema = Stores.FirstOrDefault(s => s.Name == schemaName);

            if (storeSchema == null)
            {
                throw new InvalidOperationException($"StoreSchema not found for '{schemaName}'");
            }

            // Encrypt properties with EncryptDb attribute
            var propertiesToEncrypt = typeof(T).GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(MagicEncryptAttribute), false).Length > 0);

            EncryptionFactory encryptionFactory = new EncryptionFactory(_jsRuntime, this);
            foreach (var property in propertiesToEncrypt)
            {
                if (property.PropertyType != typeof(string))
                {
                    throw new InvalidOperationException("EncryptDb attribute can only be used on string properties.");
                }

                string? originalValue = property.GetValue(record) as string;
                if (!string.IsNullOrWhiteSpace(originalValue))
                {
                    string encryptedValue = await encryptionFactory.Encrypt(originalValue, _dbStore.EncryptionKey);
                    property.SetValue(record, encryptedValue);
                }
                else
                {
                    property.SetValue(record, originalValue);
                }
            }

            // Proceed with adding the record
            if (storeSchema.PrimaryKeyAuto)
            {
                var primaryKeyProperty = typeof(T)
                    .GetProperties()
                    .FirstOrDefault(p => p.GetCustomAttributes(typeof(MagicPrimaryKeyAttribute), false).Length > 0);

                if (primaryKeyProperty != null)
                {
                    Dictionary<string, object?> recordAsDict;

                    var primaryKeyValue = primaryKeyProperty.GetValue(record);
                    if (primaryKeyValue == null || primaryKeyValue.Equals(GetDefaultValue(primaryKeyValue.GetType())))
                    {
                        recordAsDict = typeof(T).GetProperties()
                        .Where(p => p.Name != primaryKeyProperty.Name && p.GetCustomAttributes(typeof(MagicNotMappedAttribute), false).Length == 0)
                        .ToDictionary(p => p.Name, p => p.GetValue(record));
                    }
                    else
                    {
                        recordAsDict = typeof(T).GetProperties()
                        .Where(p => p.GetCustomAttributes(typeof(MagicNotMappedAttribute), false).Length == 0)
                        .ToDictionary(p => p.Name, p => p.GetValue(record));
                    }

                    // Create a new ExpandoObject and copy the key-value pairs from the dictionary
                    var expandoRecord = new ExpandoObject() as IDictionary<string, object?>;
                    foreach (var kvp in recordAsDict)
                    {
                        expandoRecord.Add(kvp);
                    }

                    return expandoRecord as ExpandoObject;
                }
            }

            return record;
        }

        // Returns the default value for the given type
        private static object? GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        //public async Task<Guid> AddRange<T>(IEnumerable<T> records, Action<BlazorDbEvent> action = null) where T : class
        //{
        //    string schemaName = SchemaHelper.GetSchemaName<T>();
        //    var propertyMappings = ManagerHelper.GeneratePropertyMapping<T>();

        //    List<object> processedRecords = new List<object>();
        //    foreach (var record in records)
        //    {
        //        object processedRecord = await ProcessRecord(record);

        //        if (processedRecord is ExpandoObject)
        //        {
        //            var convertedRecord = ((ExpandoObject)processedRecord).ToDictionary(kv => kv.Key, kv => (object)kv.Value);
        //            processedRecords.Add(ManagerHelper.ConvertPropertyNamesUsingMappings(convertedRecord, propertyMappings));
        //        }
        //        else
        //        {
        //            var convertedRecord = ManagerHelper.ConvertRecordToDictionary((T)processedRecord);
        //            processedRecords.Add(ManagerHelper.ConvertPropertyNamesUsingMappings(convertedRecord, propertyMappings));
        //        }
        //    }

        //    return await BulkAddRecord(schemaName, processedRecords, action);
        //}

        /// <summary>
        /// Adds records/objects to the specified store in bulk
        /// Waits for response
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="recordsToBulkAdd">An instance of StoreRecord that provides the store name and the data to add</param>
        /// <returns></returns>
        private Task BulkAddRecordAsync<T>(
            string storeName, 
            IEnumerable<T> recordsToBulkAdd, 
            CancellationToken cancellationToken = default)
        {
            // TODO: https://github.com/magiccodingman/Magic.IndexedDb/issues/9

            return CallJs(IndexedDbFunctions.BULKADD_ITEM, cancellationToken, [DbName, storeName, recordsToBulkAdd]);
        }

        public async Task AddRange<T>(IEnumerable<T> records) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            //var trans = GenerateTransaction(null);
            //var TableCount = await CallJavascript<int>(IndexedDbFunctions.COUNT_TABLE, trans, DbName, schemaName);
            List<Dictionary<string, object?>> processedRecords = new List<Dictionary<string, object?>>();
            foreach (var record in records)
            {
                bool IsExpando = false;
                T? myClass = null;

                object? processedRecord = await ProcessRecord(record);
                if (processedRecord is ExpandoObject)
                {
                    myClass = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(processedRecord));
                    IsExpando = true;
                }
                else
                    myClass = (T?)processedRecord;


                Dictionary<string, object?>? convertedRecord = null;
                if (processedRecord is ExpandoObject)
                {
                    var result = ((ExpandoObject)processedRecord)?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
                    if (result != null)
                        convertedRecord = result;
                }
                else
                {
                    convertedRecord = ManagerHelper.ConvertRecordToDictionary(myClass);
                }
                var propertyMappings = ManagerHelper.GeneratePropertyMapping<T>();

                // Convert the property names in the convertedRecord dictionary
                if (convertedRecord != null)
                {
                    var updatedRecord = ManagerHelper.ConvertPropertyNamesUsingMappings(convertedRecord, propertyMappings);

                    if (updatedRecord != null)
                    {
                        if (IsExpando)
                        {
                            //var test = updatedRecord.Cast<Dictionary<string, object>();
                            var dictionary = updatedRecord as Dictionary<string, object?>;
                            processedRecords.Add(dictionary);
                        }
                        else
                        {
                            processedRecords.Add(updatedRecord);
                        }
                    }
                }
            }

            await BulkAddRecordAsync(schemaName, processedRecords);
        }

        public async Task<int> UpdateAsync<T>(T item, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            PropertyInfo? primaryKeyProperty = typeof(T).GetProperties().FirstOrDefault(prop => Attribute.IsDefined(prop, typeof(MagicPrimaryKeyAttribute)));
            if (primaryKeyProperty is null)
                throw new ArgumentException("Item being updated must have a key.");

            object? primaryKeyValue = primaryKeyProperty.GetValue(item);
            var convertedRecord = ManagerHelper.ConvertRecordToDictionary(item);
            if (primaryKeyValue is null)
                throw new ArgumentException("Item being updated must have a key.");

            UpdateRecord<Dictionary<string, object?>> record = new UpdateRecord<Dictionary<string, object?>>()
            {
                Key = primaryKeyValue,
                DbName = this.DbName,
                StoreName = schemaName,
                Record = convertedRecord
            };

            return await CallJs<int>(IndexedDbFunctions.UPDATE_ITEM, cancellationToken, [record]);
        }

        public async Task<int> UpdateRangeAsync<T>(
            IEnumerable<T> items,
            CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            PropertyInfo? primaryKeyProperty = typeof(T).GetProperties().FirstOrDefault(prop => Attribute.IsDefined(prop, typeof(MagicPrimaryKeyAttribute)));

            if (primaryKeyProperty is null)
                throw new ArgumentException("Item being update range item must have a key.");

            List<UpdateRecord<Dictionary<string, object?>>> recordsToUpdate = new List<UpdateRecord<Dictionary<string, object?>>>();

            foreach (var item in items)
            {
                object? primaryKeyValue = primaryKeyProperty.GetValue(item);
                var convertedRecord = ManagerHelper.ConvertRecordToDictionary(item);

                if (primaryKeyValue is null)
                    throw new ArgumentException("Item being update range item must have a key.");

                recordsToUpdate.Add(new UpdateRecord<Dictionary<string, object?>>()
                {
                    Key = primaryKeyValue,
                    DbName = this.DbName,
                    StoreName = schemaName,
                    Record = convertedRecord
                });
            }
            return await CallJs<int>(
                IndexedDbFunctions.BULKADD_UPDATE, cancellationToken, [recordsToUpdate]);
        }

        public async Task<T?> GetByIdAsync<T>(
            object key, 
            CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            // Find the primary key property
            var primaryKeyProperty = typeof(T)
                .GetProperties()
                .FirstOrDefault(p => p.GetCustomAttributes(typeof(MagicPrimaryKeyAttribute), false).Length > 0);

            if (primaryKeyProperty == null)
            {
                throw new InvalidOperationException("No primary key property found with PrimaryKeyDbAttribute.");
            }

            // Check if the key is of the correct type
            if (!primaryKeyProperty.PropertyType.IsInstanceOfType(key))
            {
                throw new ArgumentException($"Invalid key type. Expected: {primaryKeyProperty.PropertyType}, received: {key.GetType()}");
            }

            string columnName = primaryKeyProperty.GetPropertyColumnName<MagicPrimaryKeyAttribute>();

            var data = new { DbName = DbName, StoreName = schemaName, Key = columnName, KeyValue = key };

            var propertyMappings = ManagerHelper.GeneratePropertyMapping<T>();
            var RecordToConvert = await CallJs<Dictionary<string, object>>(
                IndexedDbFunctions.FIND_ITEM, cancellationToken, [data.DbName, data.StoreName, data.KeyValue]);
            if (RecordToConvert is not null)
                return ConvertIndexedDbRecordToCRecord<T>(RecordToConvert, propertyMappings);
            else
                return default;
        }

        public MagicQuery<T> Where<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            MagicQuery<T> query = new MagicQuery<T>(schemaName, this);

            // Preprocess the predicate to break down Any and All expressions
            var preprocessedPredicate = PreprocessPredicate(predicate);
            var asdf = preprocessedPredicate.ToString();
            CollectBinaryExpressions(preprocessedPredicate.Body, preprocessedPredicate, query.JsonQueries);

            return query;
        }

        private Expression<Func<T, bool>> PreprocessPredicate<T>(Expression<Func<T, bool>> predicate)
        {
            var visitor = new PredicateVisitor<T>();
            var newExpression = visitor.Visit(predicate.Body);

            return Expression.Lambda<Func<T, bool>>(newExpression, predicate.Parameters);
        }

        internal async Task<IList<T>?> WhereV2<T>(
            string storeName, List<string> jsonQuery, MagicQuery<T> query, 
            CancellationToken cancellationToken) where T : class
        {
            string? jsonQueryAdditions = null;
            if (query != null && query.storedMagicQueries != null && query.storedMagicQueries.Count > 0)
            {
                jsonQueryAdditions = Newtonsoft.Json.JsonConvert.SerializeObject(query.storedMagicQueries.ToArray());
            }
            var propertyMappings = ManagerHelper.GeneratePropertyMapping<T>();
            IList<Dictionary<string, object>>? ListToConvert =
                await CallJs<IList<Dictionary<string, object>>>
                (IndexedDbFunctions.WHERE, cancellationToken, 
                [DbName, storeName, jsonQuery.ToArray(), jsonQueryAdditions!, query?.ResultsUnique!]);

            var resultList = ConvertListToRecords<T>(ListToConvert, propertyMappings);

            return resultList;
        }

        private void CollectBinaryExpressions<T>(Expression expression, Expression<Func<T, bool>> predicate, List<string> jsonQueries) where T : class
        {
            var binaryExpr = expression as BinaryExpression;

            if (binaryExpr != null && binaryExpr.NodeType == ExpressionType.OrElse)
            {
                // Split the OR condition into separate expressions
                var left = binaryExpr.Left;
                var right = binaryExpr.Right;

                // Process left and right expressions recursively
                CollectBinaryExpressions(left, predicate, jsonQueries);
                CollectBinaryExpressions(right, predicate, jsonQueries);
            }
            else
            {
                // If the expression is a single condition, create a query for it
                var test = expression.ToString();
                var tes2t = predicate.ToString();

                string jsonQuery = GetJsonQueryFromExpression(Expression.Lambda<Func<T, bool>>(expression, predicate.Parameters));
                jsonQueries.Add(jsonQuery);
            }
        }

        private object ConvertValueToType(object value, Type targetType)
        {
            if (targetType == typeof(Guid) && value is string stringValue)
            {
                return Guid.Parse(stringValue);
            }

            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null)
            {
                // It's nullable
                if (value == null) return null;

                return Convert.ChangeType(value, nullableType);
            }

            return Convert.ChangeType(value, targetType);
        }


        private IList<TRecord> ConvertListToRecords<TRecord>(IList<Dictionary<string, object>> listToConvert, Dictionary<string, string> propertyMappings)
        {
            var records = new List<TRecord>();
            var recordType = typeof(TRecord);

            foreach (var item in listToConvert)
            {
                var record = Activator.CreateInstance<TRecord>();

                foreach (var kvp in item)
                {
                    if (propertyMappings.TryGetValue(kvp.Key, out var propertyName))
                    {
                        var property = recordType.GetProperty(propertyName);
                        var value = ManagerHelper.GetValueFromValueKind(kvp.Value);
                        if (property != null)
                        {
                            property.SetValue(record, ConvertValueToType(value!, property.PropertyType));
                        }
                    }
                }

                records.Add(record);
            }

            return records;
        }

        private TRecord ConvertIndexedDbRecordToCRecord<TRecord>(Dictionary<string, object> item, Dictionary<string, string> propertyMappings)
        {
            var recordType = typeof(TRecord);
            var record = Activator.CreateInstance<TRecord>();

            foreach (var kvp in item)
            {
                if (propertyMappings.TryGetValue(kvp.Key, out var propertyName))
                {
                    var property = recordType.GetProperty(propertyName);
                    var value = ManagerHelper.GetValueFromValueKind(kvp.Value);
                    if (property != null)
                    {
                        property.SetValue(record, ConvertValueToType(value!, property.PropertyType));
                    }
                }
            }

            return record;
        }

        private string GetJsonQueryFromExpression<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            var serializerSettings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
            var conditions = new List<JObject>();
            var orConditions = new List<List<JObject>>();

            void TraverseExpression(Expression expression, bool inOrBranch = false)
            {
                if (expression is BinaryExpression binaryExpression)
                {
                    if (binaryExpression.NodeType == ExpressionType.AndAlso)
                    {
                        TraverseExpression(binaryExpression.Left, inOrBranch);
                        TraverseExpression(binaryExpression.Right, inOrBranch);
                    }
                    else if (binaryExpression.NodeType == ExpressionType.OrElse)
                    {
                        if (inOrBranch)
                        {
                            throw new InvalidOperationException("Nested OR conditions are not supported.");
                        }

                        TraverseExpression(binaryExpression.Left, !inOrBranch);
                        TraverseExpression(binaryExpression.Right, !inOrBranch);
                    }
                    else
                    {
                        AddCondition(binaryExpression, inOrBranch);
                    }
                }
                else if (expression is MethodCallExpression methodCallExpression)
                {
                    AddCondition(methodCallExpression, inOrBranch);
                }
            }

            void AddCondition(Expression expression, bool inOrBranch)
            {
                if (expression is BinaryExpression binaryExpression)
                {
                    var leftMember = binaryExpression.Left as MemberExpression;
                    var rightMember = binaryExpression.Right as MemberExpression;
                    var leftConstant = binaryExpression.Left as ConstantExpression;
                    var rightConstant = binaryExpression.Right as ConstantExpression;
                    var operation = binaryExpression.NodeType.ToString();

                    if (leftMember != null && rightConstant != null)
                    {
                        AddConditionInternal(leftMember, rightConstant, operation, inOrBranch);
                    }
                    else if (leftConstant != null && rightMember != null)
                    {
                        // Swap the order of the left and right expressions and the operation
                        if (operation == "GreaterThan")
                        {
                            operation = "LessThan";
                        }
                        else if (operation == "LessThan")
                        {
                            operation = "GreaterThan";
                        }
                        else if (operation == "GreaterThanOrEqual")
                        {
                            operation = "LessThanOrEqual";
                        }
                        else if (operation == "LessThanOrEqual")
                        {
                            operation = "GreaterThanOrEqual";
                        }

                        AddConditionInternal(rightMember, leftConstant, operation, inOrBranch);
                    }
                }
                else if (expression is MethodCallExpression methodCallExpression)
                {
                    if (methodCallExpression.Method.DeclaringType == typeof(string) &&
                        (methodCallExpression.Method.Name == "Equals" || methodCallExpression.Method.Name == "Contains" || methodCallExpression.Method.Name == "StartsWith"))
                    {
                        var left = methodCallExpression.Object as MemberExpression;
                        var right = methodCallExpression.Arguments[0] as ConstantExpression;
                        var operation = methodCallExpression.Method.Name;
                        var caseSensitive = true;

                        if (methodCallExpression.Arguments.Count > 1)
                        {
                            var stringComparison = methodCallExpression.Arguments[1] as ConstantExpression;
                            if (stringComparison != null && stringComparison.Value is StringComparison comparisonValue)
                            {
                                caseSensitive = comparisonValue == StringComparison.Ordinal || comparisonValue == StringComparison.CurrentCulture;
                            }
                        }

                        AddConditionInternal(left, right, operation == "Equals" ? "StringEquals" : operation, inOrBranch, caseSensitive);
                    }
                }
            }

            void AddConditionInternal(MemberExpression? left, ConstantExpression? right, string operation, bool inOrBranch, bool caseSensitive = false)
            {
                if (left != null && right != null)
                {
                    var propertyInfo = typeof(T).GetProperty(left.Member.Name);
                    if (propertyInfo != null)
                    {
                        bool index = propertyInfo.GetCustomAttributes(typeof(MagicIndexAttribute), false).Length == 0;
                        bool unique = propertyInfo.GetCustomAttributes(typeof(MagicUniqueIndexAttribute), false).Length == 0;
                        bool primary = propertyInfo.GetCustomAttributes(typeof(MagicPrimaryKeyAttribute), false).Length == 0;

                        if (index == true && unique == true && primary == true)
                        {
                            throw new InvalidOperationException($"Property '{propertyInfo.Name}' does not have the IndexDbAttribute.");
                        }

                        string? columnName = null;

                        if (index == false)
                            columnName = propertyInfo.GetPropertyColumnName<MagicIndexAttribute>();
                        else if (unique == false)
                            columnName = propertyInfo.GetPropertyColumnName<MagicUniqueIndexAttribute>();
                        else if (primary == false)
                            columnName = propertyInfo.GetPropertyColumnName<MagicPrimaryKeyAttribute>();

                        bool _isString = false;
                        JToken? valSend = null;
                        if (right != null && right.Value != null)
                        {
                            valSend = JToken.FromObject(right.Value);
                            _isString = right.Value is string;
                        }

                        var jsonCondition = new JObject
            {
                { "property", columnName },
                { "operation", operation },
                { "value", valSend },
                { "isString", _isString },
                { "caseSensitive", caseSensitive }
                        };

                        if (inOrBranch)
                        {
                            var currentOrConditions = orConditions.LastOrDefault();
                            if (currentOrConditions == null)
                            {
                                currentOrConditions = new List<JObject>();
                                orConditions.Add(currentOrConditions);
                            }
                            currentOrConditions.Add(jsonCondition);
                        }
                        else
                        {
                            conditions.Add(jsonCondition);
                        }
                    }
                }
            }

            TraverseExpression(predicate.Body);

            if (conditions.Any())
            {
                orConditions.Add(conditions);
            }

            return JsonConvert.SerializeObject(orConditions, serializerSettings);
        }

        public sealed record QuotaUsage(long Quota, long Usage)
        {
            private static double ConvertBytesToMegabytes(long bytes)
            {
                return (double)bytes / (1024 * 1024);
            }

            public double QuotaInMegabytes => ConvertBytesToMegabytes(Quota);
            public double UsageInMegabytes => ConvertBytesToMegabytes(Usage);

            public (double quota, double usage) InMegabytes => (QuotaInMegabytes, UsageInMegabytes);
        }

        /// <summary>
        /// Returns Mb
        /// </summary>
        /// <returns></returns>
        public Task<QuotaUsage> GetStorageEstimateAsync(CancellationToken cancellationToken = default)
        {
            return CallJs<QuotaUsage>(IndexedDbFunctions.GET_STORAGE_ESTIMATE, cancellationToken, []);
        }



        public async Task<IEnumerable<T>> GetAllAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            var propertyMappings = ManagerHelper.GeneratePropertyMapping<T>();
            var ListToConvert = await CallJs<IList<Dictionary<string, object>>>(
                IndexedDbFunctions.TOARRAY, cancellationToken, [DbName, schemaName]);

            var resultList = ConvertListToRecords<T>(ListToConvert, propertyMappings);
            return resultList;
        }

        public async Task DeleteAsync<T>(T item, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            PropertyInfo? primaryKeyProperty = typeof(T).GetProperties().FirstOrDefault(prop => Attribute.IsDefined(prop, typeof(MagicPrimaryKeyAttribute)));
            if (primaryKeyProperty is null)
                throw new ArgumentException("Item being Deleted must have a key.");

            object? primaryKeyValue = primaryKeyProperty.GetValue(item);
            if (primaryKeyValue is null)
                throw new ArgumentException("Item being Deleted must have a key.");

            var convertedRecord = ManagerHelper.ConvertRecordToDictionary(item);
            UpdateRecord<Dictionary<string, object?>> record = new UpdateRecord<Dictionary<string, object?>>()
            {
                Key = primaryKeyValue,
                DbName = this.DbName,
                StoreName = schemaName,
                Record = convertedRecord
            };

            await CallJs(IndexedDbFunctions.DELETE_ITEM, cancellationToken, [record]);
        }

        public async Task<int> DeleteRangeAsync<T>(
            IEnumerable<T> items, CancellationToken cancellationToken = default) where T : class
        {
            PropertyInfo? primaryKeyProperty = typeof(T).GetProperties().FirstOrDefault(prop => Attribute.IsDefined(prop, typeof(MagicPrimaryKeyAttribute)));
            if (primaryKeyProperty is null)
                throw new ArgumentException("No primary key property found with PrimaryKeyDbAttribute.");

            List<object> keys = new List<object>();
            foreach (var item in items)
            {
                object? primaryKeyValue = primaryKeyProperty.GetValue(item);
                if (primaryKeyValue is null)
                    throw new ArgumentException("Item being Deleted must have a key.");
                keys.Add(primaryKeyValue);
            }

            string schemaName = SchemaHelper.GetSchemaName<T>();
            return await CallJs<int>(
                IndexedDbFunctions.BULK_DELETE, cancellationToken,
                [DbName, schemaName, keys]);
        }

        /// <summary>
        /// Clears all data from a Table but keeps the table
        /// Wait for response
        /// </summary>
        /// <param name="storeName"></param>
        /// <returns></returns>
        public Task ClearTableAsync(string storeName, CancellationToken cancellationToken = default)
        {
            return CallJs(IndexedDbFunctions.CLEAR_TABLE, cancellationToken, [DbName, storeName]);
        }

        /// <summary>
        /// Clears all data from a Table but keeps the table
        /// Wait for response
        /// </summary>
        /// <returns></returns>
        public Task ClearTableAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            return ClearTableAsync(SchemaHelper.GetSchemaName<T>(), cancellationToken);
        }

        async Task CallJavascriptVoid(string functionName, Guid transaction, params object[] args)
        {
            var mod = await this.JsModule;
            var newArgs = GetNewArgs(transaction, args);
            await mod.InvokeVoidAsync($"{functionName}", newArgs);
        }
        async Task CallJs(string functionName, CancellationToken token, object[] args)
        {
            var mod = await this.JsModule;
            await mod.InvokeVoidAsync(functionName, token, args);
        }
        async Task<T> CallJs<T>(string functionName, CancellationToken token, object[] args)
        {
            var mod = await this.JsModule;
            return await mod.InvokeAsync<T>(functionName, token, args);
        }

        object[] GetNewArgs(Guid transaction, params object[] args)
        {
            var newArgs = new object[args.Length + 2];
            newArgs[0] = _objReference;
            newArgs[1] = transaction;
            for (var i = 0; i < args.Length; i++)
                newArgs[i + 2] = args[i];
            return newArgs;
        }
    }
}
