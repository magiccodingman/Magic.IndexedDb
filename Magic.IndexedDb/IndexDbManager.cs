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

namespace Magic.IndexedDb
{
    /// <summary>
    /// Provides functionality for accessing IndexedDB from Blazor application
    /// </summary>
    public sealed class IndexedDbManager
    {
        internal static async ValueTask<IndexedDbManager> CreateAndOpenAsync(
            DbStore dbStore, IJSObjectReference jsRuntime,
            CancellationToken cancellationToken = default)
        {
            var result = new IndexedDbManager(dbStore, jsRuntime);
            await result.CallJsAsync(IndexedDbFunctions.CREATE_DB, cancellationToken, new TypedArgument<DbStore>(dbStore));
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
        public string CurrentVersion => _dbStore.Version;
        public string DbName => _dbStore.Name;

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
            return CallJsAsync(IndexedDbFunctions.DELETE_DB, cancellationToken, new TypedArgument<string>(dbName));
        }

        public async Task AddAsync<T>(T record, CancellationToken cancellationToken = default) where T : class
        {
            _ = await AddAsync<T, JsonElement>(record, cancellationToken);
        }

        public async Task<TKey> AddAsync<T, TKey>(T record, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            StoreRecord<T?> RecordToSend = new StoreRecord<T?>()
            {
                DbName = this.DbName,
                StoreName = schemaName,
                Record = record
            };
            return await CallJsAsync<TKey>(IndexedDbFunctions.ADD_ITEM, cancellationToken, new TypedArgument<StoreRecord<T?>>(RecordToSend));
        }

        public async Task<string> DecryptAsync(
            string EncryptedValue, CancellationToken cancellationToken = default)
        {
            EncryptionFactory encryptionFactory = new EncryptionFactory(this);
            string decryptedValue = await encryptionFactory.DecryptAsync(
                EncryptedValue, _dbStore.EncryptionKey, cancellationToken);
            return decryptedValue;
        }

        private async Task<object> ProcessRecordAsync<T>(
            T record, CancellationToken cancellationToken) where T : class
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

            EncryptionFactory encryptionFactory = new EncryptionFactory(this);
            foreach (var property in propertiesToEncrypt)
            {
                if (property.PropertyType != typeof(string))
                {
                    throw new InvalidOperationException("EncryptDb attribute can only be used on string properties.");
                }

                string? originalValue = property.GetValue(record) as string;
                if (!string.IsNullOrWhiteSpace(originalValue))
                {
                    string encryptedValue = await encryptionFactory.EncryptAsync(
                        originalValue, _dbStore.EncryptionKey, cancellationToken);
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

                    return expandoRecord;
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

            return CallJsAsync(IndexedDbFunctions.BULKADD_ITEM, cancellationToken,
                new ITypedArgument[] { new TypedArgument<string>(DbName), new TypedArgument<IEnumerable<T>>(recordsToBulkAdd) });
        }

        public async Task AddRangeAsync<T>(
            IEnumerable<T> records, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            await BulkAddRecordAsync(schemaName, records, cancellationToken);
        }

        public async Task<int> UpdateAsync<T>(T item, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            object? primaryKeyValue = AttributeHelpers.GetPrimaryKeyValue<T>(item);
            if (primaryKeyValue is null)
                throw new ArgumentException("Item being updated must have a key.");

            UpdateRecord<T> record = new UpdateRecord<T>()
            {
                Key = primaryKeyValue,
                DbName = this.DbName,
                StoreName = schemaName,
                Record = item
            };

            return await CallJsAsync<int>(IndexedDbFunctions.UPDATE_ITEM, cancellationToken, new TypedArgument<UpdateRecord<T?>>(record));
        }

        public async Task<int> UpdateRangeAsync<T>(
    IEnumerable<T> items,
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
                    DbName = this.DbName,
                    StoreName = schemaName,
                    Record = item
                };
            });

            return await CallJsAsync<int>(
                IndexedDbFunctions.BULKADD_UPDATE, cancellationToken, new TypedArgument<IEnumerable<UpdateRecord<T>>>(recordsToUpdate));
        }


        public async Task<T?> GetByIdAsync<T>(
            object key,
            CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            // Validate key type
            AttributeHelpers.ValidatePrimaryKey<T>(key);

            return await CallJsAsync<T>(
                IndexedDbFunctions.FIND_ITEM, cancellationToken,
                new ITypedArgument[] { new TypedArgument<string>(DbName), new TypedArgument<string>(schemaName), new TypedArgument<object>(key) });
        }

        public MagicQuery<T> Where<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            MagicQuery<T> query = new MagicQuery<T>(schemaName, this);

            // Preprocess the predicate to break down Any and All expressions
            var preprocessedPredicate = PreprocessPredicate(predicate);
            CollectBinaryExpressions(preprocessedPredicate.Body, preprocessedPredicate, query.JsonQueries);

            return query;
        }

        private Expression<Func<T, bool>> PreprocessPredicate<T>(Expression<Func<T, bool>> predicate)
        {
            var visitor = new PredicateVisitor<T>();
            var newExpression = visitor.Visit(predicate.Body);

            return Expression.Lambda<Func<T, bool>>(newExpression, predicate.Parameters);
        }

        internal async Task<IList<T>?> WhereV2Async<T>(
            string storeName, List<string> jsonQuery, MagicQuery<T> query,
            CancellationToken cancellationToken) where T : class
        {
            string? jsonQueryAdditions = null;
            if (query != null && query.storedMagicQueries != null && query.storedMagicQueries.Count > 0)
            {
                jsonQueryAdditions = MagicSerializationHelper.SerializeObject(query.storedMagicQueries.ToArray());
            }

            var args = new ITypedArgument[] {
                new TypedArgument<string>(DbName),
                new TypedArgument<string>(storeName),
                new TypedArgument<string[]>(jsonQuery.ToArray()),
                new TypedArgument<string>(jsonQueryAdditions!),
                new TypedArgument<bool?>(query?.ResultsUnique!),
            };

            return await CallJsAsync<IList<T>>
                (IndexedDbFunctions.WHERE, cancellationToken,
                args);
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
            if (targetType.IsEnum)
            {
                return Enum.ToObject(targetType, Convert.ToInt64(value));
            }

            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null)
            {
                // It's nullable
                if (value == null)
                    return null;

                return Convert.ChangeType(value, nullableType);
            }
            return Convert.ChangeType(value, targetType);
        }

        private string GetJsonQueryFromExpression<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            var serializerSettings = new MagicJsonSerializationSettings
            {
                UseCamelCase = true // Equivalent to setting CamelCasePropertyNamesContractResolver
            };

            var conditions = new List<JsonObject>();
            var orConditions = new List<List<JsonObject>>();

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

            bool IsParameterMember(Expression expression) => expression is MemberExpression { Expression: ParameterExpression };

            ConstantExpression ToConstantExpression(Expression expression) =>
                expression switch
                {
                    ConstantExpression constantExpression => constantExpression,
                    MemberExpression memberExpression => Expression.Constant(Expression.Lambda(memberExpression).Compile().DynamicInvoke()),
                    _ => throw new InvalidOperationException($"Unsupported expression type. Expression: {expression}")
                };

            void AddCondition(Expression expression, bool inOrBranch)
            {
                if (expression is BinaryExpression binaryExpression)
                {
                    var operation = binaryExpression.NodeType.ToString();

                    if (IsParameterMember(binaryExpression.Left) && !IsParameterMember(binaryExpression.Right))
                    {
                        AddConditionInternal(
                            binaryExpression.Left as MemberExpression,
                            ToConstantExpression(binaryExpression.Right),
                            operation,
                            inOrBranch);
                    }
                    else if (!IsParameterMember(binaryExpression.Left) && IsParameterMember(binaryExpression.Right))
                    {
                        // Swap the order of the left and right expressions and the operation
                        operation = operation switch
                        {
                            "GreaterThan" => "LessThan",
                            "LessThan" => "GreaterThan",
                            "GreaterThanOrEqual" => "LessThanOrEqual",
                            "LessThanOrEqual" => "GreaterThanOrEqual",
                            _ => operation
                        };

                        AddConditionInternal(
                            binaryExpression.Right as MemberExpression,
                            ToConstantExpression(binaryExpression.Left),
                            operation,
                            inOrBranch);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unsupported binary expression. Expression: {expression}");
                    }
                }
                else if (expression is MethodCallExpression methodCallExpression)
                {
                    if (methodCallExpression.Method.DeclaringType == typeof(string) &&
                        (methodCallExpression.Method.Name == "Equals" || methodCallExpression.Method.Name == "Contains" || methodCallExpression.Method.Name == "StartsWith"))
                    {
                        var left = methodCallExpression.Object as MemberExpression;
                        var right = ToConstantExpression(methodCallExpression.Arguments[0]);
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
                    else if (methodCallExpression.Method.DeclaringType == typeof(List<string>) &&
                        methodCallExpression.Method.Name == "Contains")
                    {
                        var collection = ToConstantExpression(methodCallExpression.Object!);
                        var property = methodCallExpression.Arguments[0] as MemberExpression;
                        AddConditionInternal(property, collection, "In", inOrBranch);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unsupported method call expression. Expression: {expression}");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported expression type. Expression: {expression}");
                }
            }

            void AddConditionInternal(MemberExpression? left, ConstantExpression? right, string operation, bool inOrBranch, bool caseSensitive = false)
            {
                if (left != null && right != null)
                {
                    var propertyInfo = typeof(T).GetProperty(left.Member.Name);
                    if (propertyInfo != null)
                    {
                        bool _isString = false;
                        JsonNode? valSend = null;
                        if (right != null && right.Value != null)
                        {
                            valSend = JsonValue.Create(right.Value);
                            _isString = right.Value is string;
                        }

                        var jsonCondition = new JsonObject
            {
                { "property", PropertyMappingCache.GetJsPropertyName<T>(propertyInfo) },
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
                                currentOrConditions = new List<JsonObject>();
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

            return MagicSerializationHelper.SerializeObject(orConditions, serializerSettings);
        }

        /// <summary>
        /// Returns Mb
        /// </summary>
        /// <returns></returns>
        public Task<QuotaUsage> GetStorageEstimateAsync(CancellationToken cancellationToken = default)
        {
            return CallJsAsync<QuotaUsage>(IndexedDbFunctions.GET_STORAGE_ESTIMATE, cancellationToken, []);
        }

        public async Task<IEnumerable<T>> GetAllAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            return await CallJsAsync<IList<T>>(
                IndexedDbFunctions.TOARRAY, cancellationToken,
                new ITypedArgument[] { new TypedArgument<string>(DbName), new TypedArgument<string>(schemaName) });
        }

        public async Task DeleteAsync<T>(T item, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            object? primaryKeyValue = AttributeHelpers.GetPrimaryKeyValue<T>(item);

            UpdateRecord<T> record = new UpdateRecord<T>()
            {
                Key = primaryKeyValue,
                DbName = this.DbName,
                StoreName = schemaName,
                Record = item
            };

            await CallJsAsync(IndexedDbFunctions.DELETE_ITEM, cancellationToken, new TypedArgument<UpdateRecord<T?>>(record));
        }

        public async Task<int> DeleteRangeAsync<T>(
    IEnumerable<T> items, CancellationToken cancellationToken = default) where T : class
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
                new TypedArgument<string>(DbName),
                new TypedArgument<string>(schemaName),
                new TypedArgument<IEnumerable<object>?>(keys) };

            return await CallJsAsync<int>(
                IndexedDbFunctions.BULK_DELETE, cancellationToken,
                args);
        }


        /// <summary>
        /// Clears all data from a Table but keeps the table
        /// Wait for response
        /// </summary>
        /// <param name="storeName"></param>
        /// <returns></returns>
        public Task ClearTableAsync(string storeName, CancellationToken cancellationToken = default)
        {
            return CallJsAsync(IndexedDbFunctions.CLEAR_TABLE, cancellationToken,
                new ITypedArgument[] { new TypedArgument<string>(DbName), new TypedArgument<string>(storeName) });
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

        internal async Task CallJsAsync(string functionName, CancellationToken token, params ITypedArgument[] args)
        {
            string[] serializedArgs = MagicSerializationHelper.SerializeObjects(args);
            await this._jsModule.InvokeVoidAsync(functionName, token, serializedArgs);
        }

        internal async Task<T> CallJsAsync<T>(string functionName, CancellationToken token, params ITypedArgument[] args)
        {
            string[] serializedArgs = MagicSerializationHelper.SerializeObjects(args);
            return await this._jsModule.InvokeAsync<T>(functionName, token, serializedArgs);
        }
    }
}