using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models.UniversalOperations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Magic.IndexedDb.LinqTranslation.Extensions
{
    public class UniversalExpressionBuilder<T>
    {
        private readonly Expression<Func<T, bool>> _predicate;
        private readonly OrFilterGroup _orFilters = new();

        public UniversalExpressionBuilder(Expression<Func<T, bool>> predicate)
        {
            _predicate = predicate;
        }

        public NestedOrFilter Build()
        {
            var nested = new NestedOrFilter();
            CollectBinaryExpressions(_predicate.Body, nested);
            return nested;
        }

        private void CollectBinaryExpressions(Expression expression, NestedOrFilter nested)
        {
            if (expression is BinaryExpression binary && binary.NodeType == ExpressionType.OrElse)
            {
                CollectBinaryExpressions(binary.Left, nested);
                CollectBinaryExpressions(binary.Right, nested);
            }
            else
            {
                var andGroup = ParseToOrGroup(Expression.Lambda<Func<T, bool>>(expression, _predicate.Parameters));
                nested.orGroups.Add(andGroup);
            }
        }

        private OrFilterGroup ParseToOrGroup(Expression<Func<T, bool>> predicate)
        {
            var andGroup = new AndFilterGroup();
            var orGroup = new OrFilterGroup();

            void Traverse(Expression expr, bool isInOr = false)
            {
                switch (expr)
                {
                    case UnaryExpression { NodeType: ExpressionType.Not, Operand: var op }:
                        HandleNot(op, isInOr);
                        break;

                    case BinaryExpression bin:
                        if (bin.NodeType == ExpressionType.AndAlso)
                        {
                            Traverse(bin.Left, isInOr);
                            Traverse(bin.Right, isInOr);
                        }
                        else if (bin.NodeType == ExpressionType.OrElse)
                        {
                            if (isInOr) throw new InvalidOperationException("Nested OR conditions not supported.");
                            Traverse(bin.Left, true);
                            Traverse(bin.Right, true);
                        }
                        else
                        {
                            AddBinary(bin, isInOr);
                        }
                        break;

                    case MethodCallExpression call:
                        AddMethodCall(call, isInOr);
                        break;
                }
            }

            void HandleNot(Expression inner, bool isInOr)
            {
                switch (inner)
                {
                    case BinaryExpression bin:
                        var op = bin.NodeType switch
                        {
                            ExpressionType.GreaterThan => "LessThanOrEqual",
                            ExpressionType.LessThan => "GreaterThanOrEqual",
                            ExpressionType.GreaterThanOrEqual => "LessThan",
                            ExpressionType.LessThanOrEqual => "GreaterThan",
                            ExpressionType.Equal => "NotEquals",
                            ExpressionType.NotEqual => "StringEquals", // treat double negation of != as normal ==
                            _ => throw new InvalidOperationException($"Unsupported NOT binary: {bin}")
                        };

                        AddConditionInternal(bin.Left as MemberExpression, ToConst(bin.Right), op, isInOr);
                        break;

                    case MethodCallExpression call:
                        if (SupportedMethodNameForNegation(call.Method.Name))
                        {
                            string inverted = Invert(call.Method.Name);
                            var left = call.Object as MemberExpression;
                            var right = ToConst(call.Arguments[0]);
                            bool caseSensitive = ExtractCaseSensitivity(call);
                            AddConditionInternal(left, right, inverted, isInOr, caseSensitive);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unsupported NOT call: {call}");
                        }
                        break;

                    default:
                        throw new InvalidOperationException($"Unsupported NOT: {inner}");
                }
            }

            void AddBinary(BinaryExpression bin, bool isInOr)
            {
                string op = bin.NodeType.ToString();
                if (IsParamMember(bin.Left) && !IsParamMember(bin.Right))
                {
                    AddConditionInternal(bin.Left as MemberExpression, ToConst(bin.Right), op, isInOr);
                }
                else if (!IsParamMember(bin.Left) && IsParamMember(bin.Right))
                {
                    op = InvertBinary(op);
                    AddConditionInternal(bin.Right as MemberExpression, ToConst(bin.Left), op, isInOr);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported binary expression: {bin}");
                }
            }

            void AddMethodCall(MethodCallExpression call, bool isInOr)
            {
                // Flatten value-array.Contains(x.Property) or x.Collection.Contains(value)
                if (TryFlattenContains(call, isInOr, out var flattened))
                {
                    foreach (var cond in flattened)
                    {
                        var individualGroup = new AndFilterGroup();
                        individualGroup.conditions.Add(cond);
                        orGroup.andGroups.Add(individualGroup); // Treat them as parallel OR options
                    }
                    return;
                }

                string name = call.Method.Name;
                string resolvedOp = name switch
                {
                    "Equals" => "StringEquals",
                    _ => name
                };

                bool caseSensitive = ExtractCaseSensitivity(call);

                // Extended methods like GetDay(), IsNull(), etc.
                if (SupportedUnaryMethod(resolvedOp))
                {
                    var left = call.Arguments.FirstOrDefault() as MemberExpression ?? call.Object as MemberExpression;
                    var right = call.Arguments.ElementAtOrDefault(1) as ConstantExpression;
                    AddConditionInternal(left, right, resolvedOp, isInOr, caseSensitive);
                }
                // Built-in string methods like Contains("bo", ...)
                else if (resolvedOp is "StringEquals" or "Contains" or "StartsWith" or "EndsWith")
                {
                    var left = call.Object as MemberExpression;
                    var right = ToConst(call.Arguments[0]);
                    AddConditionInternal(left, right, resolvedOp, isInOr, caseSensitive);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported method: {call}");
                }
            }


            ConstantExpression ToConst(Expression expr) => expr switch
            {
                ConstantExpression c => c,
                MemberExpression m => Expression.Constant(Expression.Lambda(m).Compile().DynamicInvoke()),
                _ => throw new InvalidOperationException($"Unsupported or non-constant: {expr}")
            };

            bool IsParamMember(Expression e) => e is MemberExpression { Expression: ParameterExpression };

            void AddConditionInternal(MemberExpression? left, ConstantExpression? right, string op, bool isOr, bool caseSensitive = false)
            {
                if (left == null || right == null) return;
                var prop = typeof(T).GetProperty(left.Member.Name);
                if (prop == null) return;

                bool isString = right.Value is string;
                JsonNode? val = right.Value != null ? JsonValue.Create(right.Value) : null;
                string jsProp = PropertyMappingCache.GetJsPropertyName<T>(prop);
                var cond = new FilterCondition(jsProp, op, val, isString, caseSensitive);

                if (isOr)
                {
                    var group = orGroup.andGroups.LastOrDefault() ?? new AndFilterGroup();
                    if (!orGroup.andGroups.Contains(group)) orGroup.andGroups.Add(group);
                    group.conditions.Add(cond);
                }
                else
                {
                    andGroup.conditions.Add(cond);
                }
            }

            Traverse(predicate.Body);

            if (andGroup.conditions.Any())
                orGroup.andGroups.Add(andGroup);

            return orGroup;
        }


        private bool TryFlattenContains(MethodCallExpression call, bool isOr, out IEnumerable<FilterCondition> flattened)
        {
            flattened = Enumerable.Empty<FilterCondition>();

            if (call.Method.Name != "Contains")
                return false;

            // Case 1: Static-style — myArray.Contains(x._Age)
            if (call.Object == null && call.Arguments.Count == 2)
            {
                var maybeCollection = call.Arguments[0];
                var maybeProp = call.Arguments[1];

                if ((maybeCollection is MemberExpression || maybeCollection is ConstantExpression) &&
                    maybeProp is MemberExpression propExpr &&
                    IsParamMember(propExpr))
                {
                    var collectionExpr = maybeCollection;
                    var collection = Expression.Lambda(collectionExpr).Compile().DynamicInvoke();

                    if (collection is IEnumerable enumerable)
                    {
                        var propInfo = typeof(T).GetProperty(propExpr.Member.Name);
                        if (propInfo == null) 
                            return false;

                        string jsProp = PropertyMappingCache.GetJsPropertyName<T>(propInfo);

                        flattened = enumerable
                            .Cast<object?>()
                            .Select(val =>
                                new FilterCondition(
                                    jsProp,
                                    "Equal",
                                    val != null ? JsonValue.Create(val) : null,
                                    val is string,
                                    false
                                )
                            );

                        return true;
                    }
                }
            }

            // Case 2: Instance-style — x.CollectionProperty.Contains(10)
            if (call.Object is MemberExpression collectionMember &&
                call.Arguments.Count == 1 &&
                call.Arguments[0] is ConstantExpression constant)
            {
                var propInfo = typeof(T).GetProperty(collectionMember.Member.Name);
                if (propInfo == null) return false;

                string jsProp = PropertyMappingCache.GetJsPropertyName<T>(propInfo);

                flattened = new[]
                {
            new FilterCondition(
                jsProp,
                "ArrayContains",
                JsonValue.Create(constant.Value),
                constant.Value is string,
                false
            )
        };

                return true;
            }

            return false;
        }





        private static bool SupportedMethodNameForNegation(string name) =>
            name is "Contains" or "StartsWith" or "EndsWith" or "Equals";

        private static bool SupportedUnaryMethod(string name) =>
            name.StartsWith("GetDay") || name.StartsWith("TypeOf") || name.StartsWith("NotTypeOf") ||
            name.StartsWith("Length") || name.StartsWith("NotLength") ||
            name is "IsNull" or "IsNotNull" or "NotContains";

        private static bool ExtractCaseSensitivity(MethodCallExpression call)
        {
            if (call.Arguments.Count > 1 && call.Arguments[1] is ConstantExpression cmp && cmp.Value is StringComparison cmpVal)
            {
                return cmpVal == StringComparison.Ordinal || cmpVal == StringComparison.CurrentCulture;
            }
            return true;
        }

        private static string Invert(string methodName) => methodName switch
        {
            "Contains" => "NotContains",
            "StartsWith" => "NotStartsWith",
            "EndsWith" => "NotEndsWith",
            "Equals" => "NotEquals",
            _ => throw new InvalidOperationException($"Cannot invert unsupported method: {methodName}")
        };

        private static string InvertBinary(string op) => op switch
        {
            "GreaterThan" => "LessThan",
            "LessThan" => "GreaterThan",
            "GreaterThanOrEqual" => "LessThanOrEqual",
            "LessThanOrEqual" => "GreaterThanOrEqual",
            _ => op
        };

        private static ConstantExpression ToConst(Expression expr) => expr switch
        {
            ConstantExpression c => c,
            MemberExpression m => Expression.Constant(Expression.Lambda(m).Compile().DynamicInvoke()),
            _ => throw new InvalidOperationException($"Unsupported or non-constant: {expr}")
        };

        private static bool IsParamMember(Expression expr)
        {
            return expr is MemberExpression member &&
                   member.Expression is ParameterExpression;
        }


    }
    // Your supporting model classes (OrFilterGroup, AndFilterGroup, FilterCondition, etc.)
    // should stay as they are in their own files or namespaces.
}

