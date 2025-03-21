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
using Magic.IndexedDb.LinqTranslation.Extensions.TransformExpressions;

namespace Magic.IndexedDb.LinqTranslation.Extensions
{
    internal class MagicQueryExtensions<T> :
        IMagicQueryPaginationTake<T>, IMagicQueryOrderable<T>,
        IMagicQueryOrderableTable<T>, IMagicQueryFinal<T>
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
            await foreach (var item in MagicQuery.Manager.LinqToIndexedDbYield<T>(nestedOrFilter, MagicQuery, cancellationToken))
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
            return (await MagicQuery.Manager.LinqToIndexedDb<T>(
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

        public async Task<T?> FirstOrDefaultAsync()
        {
            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.additionFunction = MagicQueryFunctions.First;
            _MagicQuery.StoredMagicQueries.Add(smq);

            var items = await new MagicQueryExtensions<T>(_MagicQuery).ToListAsync();
            return items.FirstOrDefault();
        }

        public async Task<T?> LastOrDefaultAsync()
        {
            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            StoredMagicQuery smq = new StoredMagicQuery();
            smq.additionFunction = MagicQueryFunctions.Last;
            _MagicQuery.StoredMagicQueries.Add(smq);
            var items = await new MagicQueryExtensions<T>(_MagicQuery).ToListAsync();
            return items.LastOrDefault();
        }

        public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate)
        {
            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            _MagicQuery.Predicates.Add(predicate);
            return await new MagicQueryExtensions<T>(_MagicQuery).FirstOrDefaultAsync();
        }

        public async Task<T?> LastOrDefaultAsync(Expression<Func<T, bool>> predicate)
        {
            var _MagicQuery = new MagicQuery<T>(this.MagicQuery);
            _MagicQuery.Predicates.Add(predicate);
            return await new MagicQueryExtensions<T>(_MagicQuery).LastOrDefaultAsync();
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
        public IMagicQueryOrderableTable<T> OrderBy(Expression<Func<T, object>> predicate)
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
        public IMagicQueryOrderableTable<T> OrderByDescending(Expression<Func<T, object>> predicate)
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

            // Apply your new DateTime transformation visitor
            //preprocessedPredicate = TransformPredicates(preprocessedPredicate);

            // FLATTEN OR CONDITIONS because they are annoying and IndexDB doesn't support that!
            var flattenedPredicate = ExpressionFlattener.FlattenAndOptimize(preprocessedPredicate);

            CollectBinaryExpressions(flattenedPredicate.Body, flattenedPredicate, nestedOrFilter);
            return nestedOrFilter;
        }

        private Expression<Func<T, bool>> TransformPredicates(Expression<Func<T, bool>> predicate)
        {
            var visitor = new PredicateTransformationVisitor();
            var newBody = visitor.Visit(predicate.Body);
            return Expression.Lambda<Func<T, bool>>(newBody, predicate.Parameters);
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
                    else if (unaryExpression.Operand is MethodCallExpression methodCall)
                    {
                        string methodName = methodCall.Method.Name;

                        if (SupportedMethodNameForNegation(methodName))
                        {
                            if (methodCall.Arguments.Count == 0 || methodCall.Arguments[0] == null)
                            {
                                throw new InvalidOperationException($"Cannot invert method '{methodName}' — missing or null argument.");
                            }

                            string invertedOp = InvertOperation(methodName);
                            var left = methodCall.Object as MemberExpression;
                            var right = ToConstantExpression(methodCall.Arguments[0]);

                            bool caseSensitive = true;
                            if (methodCall.Arguments.Count > 1 &&
                                methodCall.Arguments[1] is ConstantExpression comparison &&
                                comparison.Value is StringComparison comparisonValue)
                            {
                                caseSensitive = comparisonValue == StringComparison.Ordinal || comparisonValue == StringComparison.CurrentCulture;
                            }

                            AddConditionInternal(left, right, invertedOp, inOrBranch, caseSensitive);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unsupported NOT operation on method call: {methodCall}");
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
                        _ => throw new InvalidOperationException($"Unsupported or non-constant expression: {expression}")
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
                else if (expression is MethodCallExpression methodCall)
                {
                    string? opName = methodCall.Method.Name;
                    bool caseSensitive = true;

                    // Normalize known operations
                    if (opName is "Equals" or "Contains" or "StartsWith" or "EndsWith")
                    {
                        var left = methodCall.Object as MemberExpression;
                        var right = ToConstantExpression(methodCall.Arguments[0]);

                        // Handle Equals(string, StringComparison)
                        if (methodCall.Arguments.Count > 1 && methodCall.Arguments[1] is ConstantExpression cmp &&
                            cmp.Value is StringComparison comparisonValue)
                        {
                            caseSensitive = comparisonValue == StringComparison.Ordinal || comparisonValue == StringComparison.CurrentCulture;
                        }

                        var resolvedOp = opName switch
                        {
                            "Equals" => "StringEquals",
                            _ => opName
                        };

                        AddConditionInternal(left, right, resolvedOp, inOrBranch, caseSensitive);
                    }
                    // Handle extended methods (e.g., GetDay(), IsNull(), TypeOfNumber(), etc.)
                    else if (SupportedUnaryMethod(methodCall.Method.Name))
                    {
                        var left = methodCall.Arguments.FirstOrDefault() as MemberExpression ?? methodCall.Object as MemberExpression;
                        var right = methodCall.Arguments.ElementAtOrDefault(1) as ConstantExpression;

                        AddConditionInternal(left, right, methodCall.Method.Name, inOrBranch);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unsupported method call expression: {methodCall}");
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

        private bool SupportedMethodNameForNegation(string name) =>
    name is "Contains" or "StartsWith" or "EndsWith" or "Equals";

        private bool SupportedUnaryMethod(string name) =>
            name.StartsWith("GetDay") || name.StartsWith("TypeOf") || name.StartsWith("NotTypeOf") ||
            name.StartsWith("Length") || name.StartsWith("NotLength") ||
            name is "IsNull" or "IsNotNull" or "NotContains";

        string InvertOperation(string methodName)
        {
            return methodName switch
            {
                "Contains" => "NotContains",
                "StartsWith" => "NotStartsWith",
                "EndsWith" => "NotEndsWith",
                "Equals" => "NotEquals",
                _ => throw new InvalidOperationException($"Cannot invert unsupported method: {methodName}")
            };
        }

    }
}
