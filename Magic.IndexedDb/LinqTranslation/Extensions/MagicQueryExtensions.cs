using Magic.IndexedDb.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Magic.IndexedDb.LinqTranslation.Interfaces;
using Magic.IndexedDb.Helpers;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Collections;

namespace Magic.IndexedDb.LinqTranslation.Extensions
{
    internal class MagicQueryExtensions<T> : IMagicQueryStage<T> where T : class
    {
        public MagicQuery<T> MagicQuery { get; set; }
        public MagicQueryExtensions(MagicQuery<T> _magicQuery)
        {
            MagicQuery = _magicQuery;

        }

        /// <summary>
        /// safe to use, but emulates an IAsync until future implementation
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async IAsyncEnumerable<T> AsAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var results = await MagicQuery.Manager.WhereV2Async<T>(MagicQuery.SchemaName, 
                JsonQueries, MagicQuery, cancellationToken);

            if (results != null)
            {
                foreach (var item in results)
                {
                    yield return item; // ✅ Stream results one at a time
                }
            }
        }

        public async Task<IEnumerable<T>> WhereAsync(
    Expression<Func<T, bool>> predicate,
    CancellationToken cancellationToken = default)
        {
            var items = await AsAsyncEnumerable(cancellationToken).ToListAsync(cancellationToken);
            return items.Where(predicate.Compile()); // Apply predicate after materialization
        }

        private List<string> JsonQueries { get => GetCollectedBinaryJsonExpressions(); }


        public async Task<List<T>> ToListAsync()
        {
            return (await MagicQuery.Manager.WhereV2Async<T>(MagicQuery.SchemaName, 
                JsonQueries, MagicQuery, default))?.ToList() ?? new List<T>();
        }

        public IMagicQueryStage<T> Take(int amount)
        {
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.Name = MagicQueryFunctions.Take;
            smq.IntValue = amount;
            MagicQuery.StoredMagicQueries.Add(smq);
            return this;
        }

        public IMagicQueryStage<T> TakeLast(int amount)
        {
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.Name = MagicQueryFunctions.Take_Last;
            smq.IntValue = amount;
            MagicQuery.StoredMagicQueries.Add(smq);
            return this;
        }

        public IMagicQueryStage<T> Skip(int amount)
        {
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.Name = MagicQueryFunctions.Skip;
            smq.IntValue = amount;
            MagicQuery.StoredMagicQueries.Add(smq);
            return this;
        }

        // Not currently available in Dexie version 1,2, or 3
        public IMagicQueryStage<T> OrderBy(Expression<Func<T, object>> predicate)
        {
            var memberExpression = GetMemberExpressionFromLambda(predicate);
            var propertyInfo = memberExpression.Member as PropertyInfo;

            if (propertyInfo == null)
            {
                throw new ArgumentException("The expression must represent a single property access.");
            }
            MagicPropertyEntry mpe = PropertyMappingCache.GetPropertyEntry<T>(propertyInfo);


            if (!mpe.PrimaryKey && !mpe.Indexed && !mpe.UniqueIndex)
            {
                throw new ArgumentException("The selected property must have either MagicIndexAttribute, MagicUniqueIndexAttribute, or MagicPrimaryKeyAttribute.");
            }

            StoredMagicQuery smq = new StoredMagicQuery();
            smq.Name = MagicQueryFunctions.Order_By;
            smq.StringValue = mpe.JsPropertyName;
            MagicQuery.StoredMagicQueries.Add(smq);
            return this;
        }

        // Not currently available in Dexie version 1,2, or 3
        public IMagicQueryStage<T> OrderByDescending(Expression<Func<T, object>> predicate)
        {
            var memberExpression = GetMemberExpressionFromLambda(predicate);
            var propertyInfo = memberExpression.Member as PropertyInfo;

            if (propertyInfo == null)
            {
                throw new ArgumentException("The expression must represent a single property access.");
            }

            StoredMagicQuery smq = new StoredMagicQuery();
            smq.Name = MagicQueryFunctions.Order_By_Descending;

            // this process could be much more performant
            smq.StringValue = PropertyMappingCache.GetJsPropertyName<T>(propertyInfo);
            MagicQuery.StoredMagicQueries.Add(smq);
            return this;
        }

        private MemberExpression GetMemberExpressionFromLambda(Expression<Func<T, object>> expression)
        {
            if (expression.Body is MemberExpression)
            {
                return (MemberExpression)expression.Body;
            }
            else if (expression.Body is UnaryExpression && ((UnaryExpression)expression.Body).Operand is MemberExpression)
            {
                return (MemberExpression)((UnaryExpression)expression.Body).Operand;
            }
            else
            {
                throw new ArgumentException("The expression must represent a single property access.");
            }
        }


        private List<string> GetCollectedBinaryJsonExpressions()
        {
            List<string> jsonBinaryExpresions = new List<string>();
            var preprocessedPredicate = PreprocessPredicate();
            CollectBinaryExpressions(preprocessedPredicate.Body, preprocessedPredicate, jsonBinaryExpresions);
            return jsonBinaryExpresions;
        }

        private Expression<Func<T, bool>> PreprocessPredicate()
        {
            Expression<Func<T, bool>> predicate = MagicQuery.GetFinalPredicate();
            var visitor = new PredicateVisitor<T>();
            var newExpression = visitor.Visit(predicate.Body);

            return Expression.Lambda<Func<T, bool>>(newExpression, predicate.Parameters);
        }

        private void CollectBinaryExpressions(Expression expression, 
            Expression<Func<T, bool>> predicate, 
            List<string> jsonQueries)
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
                string jsonQuery = GetJsonQueryFromExpression(Expression.Lambda<Func<T, bool>>(expression, predicate.Parameters));
                jsonQueries.Add(jsonQuery);
            }
        }

        private string GetJsonQueryFromExpression(Expression<Func<T, bool>> predicate)
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
    }
}
