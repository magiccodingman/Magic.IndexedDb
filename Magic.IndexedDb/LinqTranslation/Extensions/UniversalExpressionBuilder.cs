using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.LinqTranslation.Models;
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

        public UniversalExpressionBuilder(Expression<Func<T, bool>> predicate)
        {
            _predicate = predicate;
        }

        /// <summary>
        /// Builds and returns the FilterNode (root) that represents the entire predicate.
        /// </summary>
        public FilterNode Build()
        {
            return ParseExpression(_predicate.Body);
        }

        private FilterNode ParseExpression(Expression expression)
        {
            return expression switch
            {
                BinaryExpression binaryExpr => ParseBinaryExpression(binaryExpr),
                MethodCallExpression methodCall => ParseMethodCallExpression(methodCall),
                UnaryExpression unaryExpr when unaryExpr.NodeType == ExpressionType.Not => ParseNotExpression(unaryExpr),
                ConstantExpression constExpr => HandleConstantBoolean(constExpr),
                MemberExpression memberExpr => HandleMemberBoolean(memberExpr),
                _ => throw new InvalidOperationException($"Unsupported expression node: {expression}")
            };
        }

        private FilterNode HandleConstantBoolean(ConstantExpression expr)
        {
            if (expr.Type == typeof(bool))
            {
                var condition = new FilterCondition(
                    _property: "__constant",
                    _operation: "Equal",
                    _value: (bool)expr.Value!,
                    _isString: false,
                    _caseSensitive: false
                );

                return new FilterNode
                {
                    NodeType = FilterNodeType.Condition,
                    Condition = condition
                };
            }

            throw new InvalidOperationException($"Unsupported constant type: {expr.Type}");
        }

        private FilterNode HandleMemberBoolean(MemberExpression memberExpr)
        {
            if (IsParameterMember(memberExpr))
            {
                var propInfo = typeof(T).GetProperty(memberExpr.Member.Name);
                if (propInfo != null && propInfo.PropertyType == typeof(bool))
                {
                    string universalProp = PropertyMappingCache.GetJsPropertyName<T>(propInfo);

                    var condition = new FilterCondition(
                        _property: universalProp,
                        _operation: "Equal",
                        _value: true,
                        _isString: false,
                        _caseSensitive: false
                    );

                    return new FilterNode
                    {
                        NodeType = FilterNodeType.Condition,
                        Condition = condition
                    };
                }
            }

            throw new InvalidOperationException($"Unsupported member expression: {memberExpr}");
        }

        private FilterNode ParseBinaryExpression(BinaryExpression bin)
        {
            if (bin.NodeType == ExpressionType.AndAlso || bin.NodeType == ExpressionType.OrElse)
            {
                var op = bin.NodeType == ExpressionType.AndAlso
                    ? FilterLogicalOperator.And
                    : FilterLogicalOperator.Or;

                return new FilterNode
                {
                    NodeType = FilterNodeType.Logical,
                    Operator = op,
                    Children = new List<FilterNode>
                {
                    ParseExpression(bin.Left),
                    ParseExpression(bin.Right)
                }
                };
            }
            else
            {
                return BuildComparisonLeaf(bin);
            }
        }

        private FilterNode ParseMethodCallExpression(MethodCallExpression call)
        {
            if (TryFlattenContains(call, out var flattenedConditions))
            {
                return new FilterNode
                {
                    NodeType = FilterNodeType.Logical,
                    Operator = FilterLogicalOperator.Or,
                    Children = flattenedConditions
                        .Select(c => new FilterNode
                        {
                            NodeType = FilterNodeType.Condition,
                            Condition = c
                        }).ToList()
                };
            }

            string name = call.Method.Name;
            string operation = name switch
            {
                "Equals" => "StringEquals",
                _ => name
            };

            bool caseSensitive = ExtractCaseSensitivity(call);

            if (operation is "Contains" or "StartsWith" or "EndsWith" or "StringEquals")
            {
                var leftExpr = call.Object as MemberExpression;
                var rightVal = ToConst(call.Arguments[0]);

                if (leftExpr == null || rightVal == null)
                    throw new InvalidOperationException($"Cannot parse method call: {call}");

                var cond = BuildConditionFromMemberAndConstant(leftExpr, rightVal, operation, caseSensitive);

                return new FilterNode
                {
                    NodeType = FilterNodeType.Condition,
                    Condition = cond
                };
            }
            else if (SupportedUnaryMethod(operation))
            {
                var leftExpr = call.Arguments.FirstOrDefault() as MemberExpression ?? call.Object as MemberExpression;
                ConstantExpression? rightVal =
                    call.Arguments.Count > 1 ? call.Arguments[1] as ConstantExpression : null;

                if (leftExpr == null)
                    throw new InvalidOperationException($"Unsupported method expression: {call}");

                var cond = BuildConditionFromMemberAndConstant(leftExpr, rightVal, operation, caseSensitive);

                return new FilterNode
                {
                    NodeType = FilterNodeType.Condition,
                    Condition = cond
                };
            }

            throw new InvalidOperationException($"Unsupported method call: {call}");
        }

        private FilterNode ParseNotExpression(UnaryExpression notExpr)
        {
            var inner = notExpr.Operand;

            return inner switch
            {
                BinaryExpression bin => HandleNotBinary(bin),
                MethodCallExpression call => HandleNotMethod(call),
                _ => throw new InvalidOperationException($"Unsupported NOT expression: {inner}")
            };
        }

        private FilterNode HandleNotBinary(BinaryExpression bin)
        {
            string invertedOp = bin.NodeType switch
            {
                ExpressionType.GreaterThan => "LessThanOrEqual",
                ExpressionType.LessThan => "GreaterThanOrEqual",
                ExpressionType.GreaterThanOrEqual => "LessThan",
                ExpressionType.LessThanOrEqual => "GreaterThan",
                ExpressionType.Equal => "NotEquals",
                ExpressionType.NotEqual => "StringEquals",
                _ => throw new InvalidOperationException($"Unsupported NOT binary: {bin}")
            };

            return BuildComparisonLeaf(bin, forceOperation: invertedOp);
        }

        private FilterNode HandleNotMethod(MethodCallExpression call)
        {
            if (!SupportedMethodNameForNegation(call.Method.Name))
                throw new InvalidOperationException($"Unsupported NOT call: {call}");

            string inverted = Invert(call.Method.Name);
            var leftExpr = call.Object as MemberExpression;
            var rightVal = ToConst(call.Arguments[0]);
            bool caseSensitive = ExtractCaseSensitivity(call);

            if (leftExpr == null || rightVal == null)
                throw new InvalidOperationException($"Cannot parse NOT method: {call}");

            var cond = BuildConditionFromMemberAndConstant(leftExpr, rightVal, inverted, caseSensitive);

            return new FilterNode
            {
                NodeType = FilterNodeType.Condition,
                Condition = cond
            };
        }

        private FilterNode BuildComparisonLeaf(BinaryExpression bin, string? forceOperation = null)
        {
            string operation = forceOperation ?? bin.NodeType.ToString();

            if (IsParameterMember(bin.Left) && !IsParameterMember(bin.Right))
            {
                var left = bin.Left as MemberExpression;
                var right = ToConst(bin.Right);
                var cond = BuildConditionFromMemberAndConstant(left, right, operation);

                return new FilterNode
                {
                    NodeType = FilterNodeType.Condition,
                    Condition = cond
                };
            }
            else if (!IsParameterMember(bin.Left) && IsParameterMember(bin.Right))
            {
                operation = InvertBinary(operation);
                var left = bin.Right as MemberExpression;
                var right = ToConst(bin.Left);
                var cond = BuildConditionFromMemberAndConstant(left, right, operation);

                return new FilterNode
                {
                    NodeType = FilterNodeType.Condition,
                    Condition = cond
                };
            }

            throw new InvalidOperationException($"Unsupported binary expression: {bin}");
        }


        // ------------------------------
        // "Contains" flattening logic:
        // ------------------------------
        private bool TryFlattenContains(MethodCallExpression call, out IEnumerable<FilterCondition> flattened)
        {
            flattened = Enumerable.Empty<FilterCondition>();

            if (call.Method.Name != "Contains")
                return false;

            // Case 1: Static-style => myArray.Contains(x.SomeProp)
            if (call.Object == null && call.Arguments.Count == 2)
            {
                var maybeCollection = call.Arguments[0];
                var maybeProp = call.Arguments[1];

                if ((maybeCollection is MemberExpression || maybeCollection is ConstantExpression) &&
                    maybeProp is MemberExpression propExpr &&
                    IsParameterMember(propExpr))
                {
                    var collection = Expression.Lambda(maybeCollection).Compile().DynamicInvoke();
                    if (collection is IEnumerable enumerable)
                    {
                        var propInfo = typeof(T).GetProperty(propExpr.Member.Name);
                        if (propInfo == null) return false;

                        // Suppose you have a way to map property name to something universal:
                        string universalProp = PropertyMappingCache.GetJsPropertyName<T>(propInfo);

                        flattened = enumerable
                            .Cast<object?>()
                            .Select(val => new FilterCondition(
                                universalProp,
                                "Equal",
                                val,
                                val is string,
                                false
                            ));
                        return true;
                    }
                }
            }

            // Case 2: Instance-style => x.CollectionProperty.Contains(10)
            if (call.Object is MemberExpression collectionMember &&
                call.Arguments.Count == 1 &&
                call.Arguments[0] is ConstantExpression constant)
            {
                var propInfo = typeof(T).GetProperty(collectionMember.Member.Name);
                if (propInfo == null) return false;

                string universalProp = PropertyMappingCache.GetJsPropertyName<T>(propInfo);

                flattened = new[]
                {
                new FilterCondition(
                    universalProp,
                    "ArrayContains",
                    constant.Value,
                    constant.Value is string,
                    false
                )
            };
                return true;
            }

            return false;
        }

        // ------------------------------
        // Internal Helpers
        // ------------------------------

        private FilterCondition BuildConditionFromMemberAndConstant(
            MemberExpression? memberExpr,
            ConstantExpression? constExpr,
            string operation,
            bool caseSensitive = false)
        {
            if (memberExpr == null || constExpr == null)
            {
                throw new InvalidOperationException("Cannot build filter condition from null expressions.");
            }

            var propInfo = typeof(T).GetProperty(memberExpr.Member.Name);
            if (propInfo == null)
            {
                throw new InvalidOperationException($"Property {memberExpr.Member.Name} not found on type {typeof(T).Name}.");
            }

            // Possibly convert to a JSON node or keep as raw object. 
            // If you absolutely need a JSON representation, do:
            // object? val = constExpr.Value != null ? JsonValue.Create(constExpr.Value) : null;
            // Otherwise, you can just store the raw object in FilterCondition.value:
            object? val = constExpr.Value;

            // e.g. "name", "age"
            string universalProp = PropertyMappingCache.GetJsPropertyName<T>(propInfo);

            // isString is relevant only if the constant is indeed a string
            bool isString = val is string;

            return new FilterCondition(
                universalProp,
                operation,
                val,
                isString,
                caseSensitive
            );
        }

        private static bool SupportedMethodNameForNegation(string name)
            => name is "Contains" or "StartsWith" or "EndsWith" or "Equals";

        private static string Invert(string methodName)
        {
            // e.g. Contains -> NotContains, StartsWith -> NotStartsWith, etc.
            return methodName switch
            {
                "Contains" => "NotContains",
                "StartsWith" => "NotStartsWith",
                "EndsWith" => "NotEndsWith",
                "Equals" => "NotEquals",
                _ => throw new InvalidOperationException($"Cannot invert unsupported method: {methodName}")
            };
        }

        private static string InvertBinary(string op)
        {
            return op switch
            {
                "GreaterThan" => "LessThan",
                "LessThan" => "GreaterThan",
                "GreaterThanOrEqual" => "LessThanOrEqual",
                "LessThanOrEqual" => "GreaterThanOrEqual",
                _ => op
            };
        }

        private static bool SupportedUnaryMethod(string name)
        {
            // Your custom logic for "GetDay", "IsNull", etc.
            return name.StartsWith("GetDay")
                || name.StartsWith("TypeOf")
                || name.StartsWith("NotTypeOf")
                || name.StartsWith("Length")
                || name.StartsWith("NotLength")
                || name is "IsNull" or "IsNotNull" or "NotContains";
        }

        private static bool ExtractCaseSensitivity(MethodCallExpression call)
        {
            // Mimic your logic that checks if a StringComparison was passed in
            if (call.Arguments.Count > 1 && call.Arguments[1] is ConstantExpression cmp && cmp.Value is StringComparison cmpVal)
            {
                // If it’s ordinal or current-culture, consider it case-sensitive
                return cmpVal == StringComparison.Ordinal || cmpVal == StringComparison.CurrentCulture;
            }
            // Otherwise default to "true" or "false" depending on your preference:
            return true;
        }

        private static bool IsParameterMember(Expression expr)
        {
            return expr is MemberExpression member &&
                   member.Expression is ParameterExpression;
        }

        private static ConstantExpression ToConst(Expression expr)
        {
            return expr switch
            {
                ConstantExpression c => c,
                MemberExpression m => Expression.Constant(Expression.Lambda(m).Compile().DynamicInvoke()),
                _ => throw new InvalidOperationException($"Unsupported or non-constant expression: {expr}")
            };
        }
    }    // should stay as they are in their own files or namespaces.
}

