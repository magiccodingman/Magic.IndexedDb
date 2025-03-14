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
using Magic.IndexedDb.Models.UniversalOperations;

namespace Magic.IndexedDb.LinqTranslation.Extensions
{
    internal class MagicQueryExtensions<T> :
        IMagicQueryPaginationTake<T>, IMagicQueryOrderable<T>, IMagicQueryFinal<T>
        where T : class
    {
        public MagicQuery<T> MagicQuery { get; set; }
        
        public MagicQueryExtensions(MagicQuery<T> _magicQuery)
        {
            MagicQuery = _magicQuery;

        }

        /// <summary>
        /// EXPERIMENTAL FEATURE: 
        /// True IAsyncEnumerable between C# Blazor and JS. How?! 
        /// It's god damn magic! IMPORTANT NOTE: the order in which items 
        /// are returned may not be the order you specified. Your ordering 
        /// is properly utilized inside of IndexDB, but the returned process 
        /// due to IndexDB limitations can't return the same order. Please re-apply 
        /// your desired ordering after your results are brought back if order is important.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async IAsyncEnumerable<T> AsAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in MagicQuery.Manager.LinqToIndedDbYield<T>(nestedOrFilter, MagicQuery, cancellationToken))
            {
                if (item is not null) // Ensure non-null items
                {
                    yield return item;
                }
            }
        }

        /// <summary>
        /// The order you apply does get applied correctly in the query, 
        /// but the returned results will not be in the same order. 
        /// If order matters, you must apply the order again on return. 
        /// This is a fundemental limitation of IndexDB. 
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IEnumerable<T>> WhereAsync(
    Expression<Func<T, bool>> predicate)
        {
            var items = await ToListAsync();
            return items.Where(predicate.Compile()); // Apply predicate after materialization
        }

        private NestedOrFilter nestedOrFilter { get => GetCollectedBinaryJsonExpressions(); }

        /// <summary>
        /// The order you apply does get applied correctly in the query, 
        /// but the returned results will not be in the same order. 
        /// If order matters, you must apply the order again on return. 
        /// This is a fundemental limitation of IndexDB. 
        /// </summary>
        /// <returns></returns>
        public async Task<List<T>> ToListAsync()
        {
            return (await MagicQuery.Manager.LinqToIndedDb<T>(
                nestedOrFilter, MagicQuery, default))?.ToList() ?? new List<T>();
        }

        public IMagicQueryPaginationTake<T> Take(int amount)
        {
            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.additionFunction = MagicQueryFunctions.Take;
            smq.intValue = amount;
            _MagicQuery.StoredMagicQueries.Add(smq);
            return new MagicQueryExtensions<T>(_MagicQuery);
        }

        public IMagicQueryFinal<T> TakeLast(int amount)
        {
            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.additionFunction = MagicQueryFunctions.Take_Last;
            smq.intValue = amount;
            _MagicQuery.StoredMagicQueries.Add(smq);
            return new MagicQueryExtensions<T>(_MagicQuery);
        }

        public IMagicQueryFinal<T> Skip(int amount)
        {
            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.additionFunction = MagicQueryFunctions.Skip;
            smq.intValue = amount;
            _MagicQuery.StoredMagicQueries.Add(smq);
            return new MagicQueryExtensions<T>(_MagicQuery);
        }

        // Not currently available in Dexie version 1,2, or 3
        public IMagicQueryOrderable<T> OrderBy(Expression<Func<T, object>> predicate)
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
                //throw new ArgumentException("The selected property must have either MagicIndexAttribute, MagicUniqueIndexAttribute, or MagicPrimaryKeyAttribute.");
            }

            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.additionFunction = MagicQueryFunctions.Order_By;
            smq.property = mpe.JsPropertyName;
            _MagicQuery.StoredMagicQueries.Add(smq);
            return new MagicQueryExtensions<T>(_MagicQuery);
        }

        // Not currently available in Dexie version 1,2, or 3
        public IMagicQueryOrderable<T> OrderByDescending(Expression<Func<T, object>> predicate)
        {
            var memberExpression = GetMemberExpressionFromLambda(predicate);
            var propertyInfo = memberExpression.Member as PropertyInfo;

            if (propertyInfo == null)
            {
                throw new ArgumentException("The expression must represent a single property access.");
            }

            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.additionFunction = MagicQueryFunctions.Order_By_Descending;

            // this process could be much more performant
            smq.property = PropertyMappingCache.GetJsPropertyName<T>(propertyInfo);
            _MagicQuery.StoredMagicQueries.Add(smq);
            return new MagicQueryExtensions<T>(_MagicQuery);
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

        private NestedOrFilter GetCollectedBinaryJsonExpressions()
        {
            NestedOrFilter nestedOrFilter = new NestedOrFilter();
            Expression<Func<T, bool>> preprocessedPredicate = PreprocessPredicate();

            // Check if the predicate is a universal false condition
            if (IsUniversalFalse(preprocessedPredicate))
            {
                return new NestedOrFilter { universalFalse = true };
            }

            // FLATTEN OR CONDITIONS because they are annoying and IndexDB doesn't support that!
            var flattenedPredicate = ExpressionFlattener.FlattenAndOptimize(preprocessedPredicate);

            CollectBinaryExpressions(flattenedPredicate.Body, flattenedPredicate, nestedOrFilter);
            return nestedOrFilter;
        }


        private bool IsUniversalFalse(Expression<Func<T, bool>> predicate)
        {
            return predicate.Body is ConstantExpression constant && constant.Value is bool value && !value;
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
            NestedOrFilter nestedOrFilters)
        {
            var binaryExpr = expression as BinaryExpression;

            if (binaryExpr != null && binaryExpr.NodeType == ExpressionType.OrElse)
            {
                // Split the OR condition into separate expressions
                var left = binaryExpr.Left;
                var right = binaryExpr.Right;

                // Process left and right expressions recursively
                CollectBinaryExpressions(left, predicate, nestedOrFilters);
                CollectBinaryExpressions(right, predicate, nestedOrFilters);
            }
            else
            {
                OrFilterGroup orFilters =
                    GetJsonQueryFromExpression(Expression.Lambda<Func<T, bool>>(expression, predicate.Parameters));
                nestedOrFilters.orGroups.Add(orFilters);
            }
        }

        private OrFilterGroup GetJsonQueryFromExpression(Expression<Func<T, bool>> predicate)
        {
            //var serializerSettings = new MagicJsonSerializationSettings
            //{
            //    UseCamelCase = true // Equivalent to setting CamelCasePropertyNamesContractResolver
            //};

            var andFilters = new AndFilterGroup();
            var orFilters = new OrFilterGroup();

            void TraverseExpression(Expression expression, bool inOrBranch = false)
            {
                if (expression is UnaryExpression unaryExpression && unaryExpression.NodeType == ExpressionType.Not)
                {
                    // Invert the expression inside NOT
                    if (unaryExpression.Operand is BinaryExpression binaryExpression)
                    {
                        string invertedOperation = binaryExpression.NodeType switch
                        {
                            ExpressionType.GreaterThan => "LessThanOrEqual",
                            ExpressionType.LessThan => "GreaterThanOrEqual",
                            ExpressionType.GreaterThanOrEqual => "LessThan",
                            ExpressionType.LessThanOrEqual => "GreaterThan",
                            _ => throw new InvalidOperationException($"Unsupported NOT binary expression: {binaryExpression}")
                        };

                        AddConditionInternal(
                            binaryExpression.Left as MemberExpression,
                            ToConstantExpression(binaryExpression.Right),
                            invertedOperation,
                            inOrBranch);
                    }
                    else if (unaryExpression.Operand is MethodCallExpression methodCallExpression)
                    {
                        // Handle NotContains, NotStartsWith, NotEquals
                        if (methodCallExpression.Method.DeclaringType == typeof(string) &&
                            (methodCallExpression.Method.Name == "Contains" ||
                             methodCallExpression.Method.Name == "StartsWith" ||
                             methodCallExpression.Method.Name == "Equals"))
                        {
                            var left = methodCallExpression.Object as MemberExpression;
                            var right = ToConstantExpression(methodCallExpression.Arguments[0]);
                            var operation = methodCallExpression.Method.Name;

                            bool caseSensitive = true;
                            if (methodCallExpression.Arguments.Count > 1)
                            {
                                if (methodCallExpression.Arguments[1] is ConstantExpression comparison &&
                                    comparison.Value is StringComparison comparisonValue)
                                {
                                    caseSensitive = comparisonValue == StringComparison.Ordinal || comparisonValue == StringComparison.CurrentCulture;
                                }
                            }

                            string invertedOperation = operation switch
                            {
                                "Contains" => "NotContains",
                                "StartsWith" => "NotStartsWith",
                                "Equals" => "NotEquals",
                                _ => throw new InvalidOperationException($"Unsupported NOT method call expression: {methodCallExpression}")
                            };

                            AddConditionInternal(left, right, invertedOperation, inOrBranch, caseSensitive);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unsupported NOT operation on method call: {methodCallExpression}");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unsupported NOT operation: {unaryExpression}");
                    }
                }
                else if (expression is BinaryExpression binaryExpression)
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

                        string property = PropertyMappingCache.GetJsPropertyName<T>(propertyInfo);

                        var queryCondition = new FilterCondition(property, operation,
                            valSend, _isString, caseSensitive);

                        if (inOrBranch)
                        {
                            var currentOrConditions = orFilters.andGroups.LastOrDefault();
                            if (currentOrConditions == null)
                            {
                                currentOrConditions = new AndFilterGroup();
                                orFilters.andGroups.Add(currentOrConditions);
                            }
                            currentOrConditions.conditions.Add(queryCondition);
                        }
                        else
                        {
                            andFilters.conditions.Add(queryCondition);
                        }
                    }
                }
            }

            TraverseExpression(predicate.Body);

            if (andFilters.conditions.Any())
            {
                orFilters.andGroups.Add(andFilters);
            }

            return orFilters;
        }
    }
}
