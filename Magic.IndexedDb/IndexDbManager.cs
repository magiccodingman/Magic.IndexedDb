using Magic.IndexedDb.Factories;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.SchemaAnnotations;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

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
            await result.CallJsAsync(IndexedDbFunctions.CREATE_DB, cancellationToken, [dbStore]);
            return result;
        }

        private readonly DbStore _dbStore;
        private readonly IJSObjectReference _jsModule;

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

        public List<StoreSchema> Stores => this._dbStore.StoreSchemas;
        public int CurrentVersion => this._dbStore.Version;
        public string DbName => this._dbStore.Name;

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
            return this.CallJsAsync(IndexedDbFunctions.DELETE_DB, cancellationToken, [dbName]);
        }

        public Task<JsonElement> AddAsync<T>(T record, CancellationToken cancellationToken = default) where T : class
        {
            return this.AddAsync<T, JsonElement>(record, cancellationToken);
        }

        public async Task<TKey> AddAsync<T, TKey>(T record, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            var processedRecord = await this.ApplyEncryptionAsync(record, cancellationToken);
            StoreRecord<T> RecordToSend = new StoreRecord<T>()
            {
                DbName = this.DbName,
                StoreName = schemaName,
                Record = processedRecord
            };
            return await this.CallJsAsync<TKey>(IndexedDbFunctions.ADD_ITEM, cancellationToken, [RecordToSend]);
        }

        public async Task<string> DecryptAsync(
            string EncryptedValue, CancellationToken cancellationToken = default)
        {
            EncryptionFactory encryptionFactory = new EncryptionFactory(this);
            string decryptedValue = await encryptionFactory.DecryptAsync(
                EncryptedValue, this._dbStore.EncryptionKey, cancellationToken);
            return decryptedValue;
        }

        private async ValueTask<T> ApplyEncryptionAsync<T>(
            T record, CancellationToken cancellationToken) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            StoreSchema? storeSchema = this.Stores.FirstOrDefault(s => s.Name == schemaName);

            if (storeSchema is null)
                throw new InvalidOperationException($"StoreSchema not found for '{schemaName}'");

            var propertiesToEncrypt = typeof(T).GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(MagicEncryptAttribute), false).Length > 0)
                .Where(p => p.PropertyType == typeof(string))
                .Where(p => p.CanWrite);
            var encryptor = new EncryptionFactory(this);
            foreach (var property in propertiesToEncrypt)
            {
                string? originalValue = property.GetValue(record) as string;
                if (originalValue is not null)
                {
                    string encryptedValue = await encryptor.EncryptAsync(
                        originalValue, this._dbStore.EncryptionKey, cancellationToken);
                    property.SetValue(record, encryptedValue);
                }
            }
            return record;
        }

        public Task<IReadOnlyList<JsonElement>> AddRangeAsync<T>(
            IEnumerable<T> records, CancellationToken cancellationToken = default) where T : class
        {
            return this.AddRangeAsync<T, JsonElement>(records);
        }

        public async Task<IReadOnlyList<TKey>> AddRangeAsync<T, TKey>(
            IEnumerable<T> records, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            var processedRecords = new List<T>();
            foreach (var record in records)
            {
                var processedRecord = await this.ApplyEncryptionAsync(record, cancellationToken);
                processedRecords.Add(processedRecord);
            }
            return await this.CallJsAsync<IReadOnlyList<TKey>>(
                IndexedDbFunctions.BULKADD_ITEM,
                cancellationToken,
                [this.DbName, schemaName, processedRecords]);
        }

        public async Task<int> UpdateAsync<T>(T item, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            PropertyInfo? primaryKeyProperty = typeof(T).GetProperties().SingleOrDefault(prop => Attribute.IsDefined(prop, typeof(MagicPrimaryKeyAttribute)));
            if (primaryKeyProperty is null)
                throw new ArgumentException("Item being updated must have a key.");

            object? primaryKeyValue = primaryKeyProperty.GetValue(item);
            if (primaryKeyValue is null)
                throw new ArgumentException("Item being updated must have a key.");

            UpdateRecord<T> record = new UpdateRecord<T>()
            {
                Key = primaryKeyValue,
                DbName = this.DbName,
                StoreName = schemaName,
                Record = item
            };

            return await this.CallJsAsync<int>(IndexedDbFunctions.UPDATE_ITEM, cancellationToken, [record]);
        }

        public async Task<int> UpdateRangeAsync<T>(
            IEnumerable<T> items,
            CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            PropertyInfo? primaryKeyProperty = typeof(T).GetProperties().SingleOrDefault(prop => Attribute.IsDefined(prop, typeof(MagicPrimaryKeyAttribute)));
            if (primaryKeyProperty is null)
                throw new ArgumentException("Item being update range item must have a key.");
            List<UpdateRecord<T>> recordsToUpdate = [];
            foreach (var item in items)
            {
                object? primaryKeyValue = primaryKeyProperty.GetValue(item);
                if (primaryKeyValue is null)
                    throw new ArgumentException("Item being update range item must have a key.");

                recordsToUpdate.Add(new UpdateRecord<T>()
                {
                    Key = primaryKeyValue,
                    DbName = this.DbName,
                    StoreName = schemaName,
                    Record = item
                });
            }
            return await this.CallJsAsync<int>(
                IndexedDbFunctions.BULKADD_UPDATE, cancellationToken, [recordsToUpdate]);
        }

        public async Task<T> GetByIdAsync<T, TKey>(
            TKey key,
            CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();

            // Find the primary key property
            var primaryKeyProperty = typeof(T)
                .GetProperties()
                .SingleOrDefault(p => p.GetCustomAttributes(typeof(MagicPrimaryKeyAttribute), false).Length > 0);

            if (primaryKeyProperty == null)
            {
                throw new InvalidOperationException("No primary key property found with PrimaryKeyDbAttribute.");
            }

            // Check if the key is of the correct type
            if (!primaryKeyProperty.PropertyType.IsInstanceOfType(key))
            {
                throw new ArgumentException($"Invalid key type. Expected: {primaryKeyProperty.PropertyType}, received: {key?.GetType()}");
            }

            string columnName = primaryKeyProperty.GetPropertyColumnName<MagicPrimaryKeyAttribute>();

            var data = new { DbName = this.DbName, StoreName = schemaName, Key = columnName, KeyValue = key };

            return await this.CallJsAsync<T>(
                IndexedDbFunctions.FIND_ITEM, cancellationToken, [data.DbName, data.StoreName, data.KeyValue]);
        }

        public MagicQuery<T> Where<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            MagicQuery<T> query = new MagicQuery<T>(schemaName, this);

            // Preprocess the predicate to break down Any and All expressions
            var preprocessedPredicate = this.PreprocessPredicate(predicate);
            var asdf = preprocessedPredicate.ToString();
            this.CollectBinaryExpressions(preprocessedPredicate.Body, preprocessedPredicate, query.JsonQueries);

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
                jsonQueryAdditions = Newtonsoft.Json.JsonConvert.SerializeObject(query.storedMagicQueries.ToArray());
            }
            return
                await this.CallJsAsync<IList<T>>
                (IndexedDbFunctions.WHERE, cancellationToken,
                [this.DbName, storeName, jsonQuery.ToArray(), jsonQueryAdditions!, query?.ResultsUnique!]);
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
                this.CollectBinaryExpressions(left, predicate, jsonQueries);
                this.CollectBinaryExpressions(right, predicate, jsonQueries);
            }
            else
            {
                // If the expression is a single condition, create a query for it
                var test = expression.ToString();
                var tes2t = predicate.ToString();

                string jsonQuery = this.GetJsonQueryFromExpression(Expression.Lambda<Func<T, bool>>(expression, predicate.Parameters));
                jsonQueries.Add(jsonQuery);
            }
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

        /// <summary>
        /// Returns Mb
        /// </summary>
        /// <returns></returns>
        public Task<QuotaUsage> GetStorageEstimateAsync(CancellationToken cancellationToken = default)
        {
            return this.CallJsAsync<QuotaUsage>(IndexedDbFunctions.GET_STORAGE_ESTIMATE, cancellationToken, []);
        }

        public async Task<IEnumerable<T>> GetAllAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            return await this.CallJsAsync<IList<T>>(
                IndexedDbFunctions.TOARRAY, cancellationToken, [this.DbName, schemaName]);
        }

        public async Task DeleteAsync<T>(T item, CancellationToken cancellationToken = default) where T : class
        {
            string schemaName = SchemaHelper.GetSchemaName<T>();
            PropertyInfo? primaryKeyProperty = typeof(T).GetProperties().SingleOrDefault(prop => Attribute.IsDefined(prop, typeof(MagicPrimaryKeyAttribute)));
            if (primaryKeyProperty is null)
                throw new ArgumentException("Item being Deleted must have a key.");

            object? primaryKeyValue = primaryKeyProperty.GetValue(item);
            if (primaryKeyValue is null)
                throw new ArgumentException("Item being Deleted must have a key.");

            UpdateRecord<T> record = new UpdateRecord<T>()
            {
                Key = primaryKeyValue,
                DbName = this.DbName,
                StoreName = schemaName,
                Record = item
            };

            await this.CallJsAsync(IndexedDbFunctions.DELETE_ITEM, cancellationToken, [record]);
        }

        public async Task<int> DeleteRangeAsync<T>(
            IEnumerable<T> items, CancellationToken cancellationToken = default) where T : class
        {
            PropertyInfo? primaryKeyProperty = typeof(T).GetProperties().SingleOrDefault(prop => Attribute.IsDefined(prop, typeof(MagicPrimaryKeyAttribute)));
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
            return await this.CallJsAsync<int>(
                IndexedDbFunctions.BULK_DELETE, cancellationToken,
                [this.DbName, schemaName, keys]);
        }

        /// <summary>
        /// Clears all data from a Table but keeps the table
        /// Wait for response
        /// </summary>
        /// <param name="storeName"></param>
        /// <returns></returns>
        public Task ClearTableAsync(string storeName, CancellationToken cancellationToken = default)
        {
            return this.CallJsAsync(IndexedDbFunctions.CLEAR_TABLE, cancellationToken, [this.DbName, storeName]);
        }

        /// <summary>
        /// Clears all data from a Table but keeps the table
        /// Wait for response
        /// </summary>
        /// <returns></returns>
        public Task ClearTableAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            return this.ClearTableAsync(SchemaHelper.GetSchemaName<T>(), cancellationToken);
        }

        internal async Task CallJsAsync(string functionName, CancellationToken token, object[] args)
        {
            await this._jsModule.InvokeVoidAsync(functionName, token, args);
        }

        internal async Task<T> CallJsAsync<T>(string functionName, CancellationToken token, object[] args)
        {
            return await this._jsModule.InvokeAsync<T>(functionName, token, args);
        }
    }
}
