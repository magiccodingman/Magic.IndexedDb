using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.LinqTranslation.Models;
using Magic.IndexedDb.Models;
using Magic.IndexedDb.Models.UniversalOperations;
using System.Collections;
using System.Linq.Expressions;

namespace Magic.IndexedDb.LinqTranslation.Extensions;

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

        if (inner is BinaryExpression bin)
        {
            if (bin.NodeType == ExpressionType.OrElse || bin.NodeType == ExpressionType.AndAlso)
            {
                // De Morgan’s Law: !(A || B) => !A && !B,  !(A && B) => !A || !B
                var invertedOperator = bin.NodeType == ExpressionType.OrElse
                    ? FilterLogicalOperator.And
                    : FilterLogicalOperator.Or;

                return new FilterNode
                {
                    NodeType = FilterNodeType.Logical,
                    Operator = invertedOperator,
                    Children = new List<FilterNode>
                    {
                        ParseNotExpression(Expression.Not(bin.Left)),
                        ParseNotExpression(Expression.Not(bin.Right))
                    }
                };
            }
            else
            {
                return HandleNotBinary(bin);
            }
        }
        else if (inner is MethodCallExpression call)
        {
            return HandleNotMethod(call);
        }

        throw new InvalidOperationException($"Unsupported NOT expression: {inner}");
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

        if (TryRecognizeSpecialOperation(bin, operation, out var specialNode))
        {
            return specialNode;
        }

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

    private bool TryRecognizeSpecialOperation(BinaryExpression bin, string operation, out FilterNode node)
    {
        node = null!;

        if (TryRecognizeLengthProperty(bin, operation, out node))
            return true;

        // Recognize things like: x => x.DateOfBirth.Value.Year >= 2020
        if (TryRecognizeDateProperty(bin, operation, out node))
            return true;

        // Future recognizers:
        // if (TryRecognizeEnumFlag(bin, operation, out node)) return true;

        return false;
    }

    private bool TryRecognizeLengthProperty(BinaryExpression bin, string operation, out FilterNode node)
    {
        node = null!;
        var memberPath = GetMemberAccessPath(bin.Left);

        if (memberPath == null || memberPath.Count == 0)
            return false;

        // Error if .Value is the last thing appended (illegal usage)
        if (memberPath[^1] == "Value")
        {
            throw new InvalidOperationException("You cannot end an expression with '.Value'. Only specific extensions like '.Length' are allowed.");
        }

        // Only allow if the final segment is "Length"
        if (memberPath[^1] != "Length")
            return false;

        // Must have at least 2 parts: root + Length
        if (memberPath.Count < 2)
            return false;

        SearchPropEntry spe = PropertyMappingCache.GetTypeOfTProperties(typeof(T));

        //string rootPropName = memberPath[memberPath.Count - 2]; // i.e., "Name"
        string rootPropName = ExtractRootProperty(memberPath);

        MagicPropertyEntry mpe = spe.GetPropertyByCsharpName(rootPropName);
        string jsProp = mpe.JsPropertyName;

            
        var value = ToConst(bin.Right).Value;
        if (value == null || value is not int)
            return false;

        string op = operation switch
        {
            "Equal" => "LengthEqual",
            "NotEqual" => "NotLengthEqual",
            "GreaterThan" => "LengthGreaterThan",
            "GreaterThanOrEqual" => "LengthGreaterThanOrEqual",
            "LessThan" => "LengthLessThan",
            "LessThanOrEqual" => "LengthLessThanOrEqual",
            _ => throw new InvalidOperationException($"Unsupported operator '{operation}' for .Length")
        };

        // TODO: Replace this with your isString detection logic
        bool isString = mpe.Property.PropertyType == typeof(string);

        node = new FilterNode
        {
            NodeType = FilterNodeType.Condition,
            Condition = new FilterCondition(
                jsProp,
                op,
                value,
                isString,
                false
            )
        };

        return true;
    }

    private static string ExtractRootProperty(List<string> path)
    {
        if (path.Count < 2)
            throw new InvalidOperationException("Invalid member access path.");

        return path[^2] == "Value" && path.Count >= 3
            ? path[^3]
            : path[^2];
    }

    private bool TryRecognizeDateProperty(BinaryExpression bin, string operation, out FilterNode node)
    {
        node = null!;

        Expression leftExpr = UnwrapConvert(bin.Left);
        var memberPath = GetMemberAccessPath(leftExpr);
        if (memberPath == null || memberPath.Count < 2)
            return false;


        var finalSegment = memberPath[^1];
        var rootSegment = memberPath[^2];

        SearchPropEntry spe = PropertyMappingCache.GetTypeOfTProperties(typeof(T));

        string rootPropName =  ExtractRootProperty(memberPath);
        MagicPropertyEntry mpe = spe.GetPropertyByCsharpName(rootPropName);

        string jsProp = mpe.JsPropertyName;            

        if (!IsDateType(mpe.Property.PropertyType))
            return false;

        var rightUnwrapped = UnwrapConvert(bin.Right);
        object? rawConst = ToConst(rightUnwrapped).Value;

        if (rawConst == null)
            return false;

        switch (finalSegment)
        {
            case "Date":
                node = BuildDateEqualityRange(jsProp, rawConst, operation);
                return true;

            case "Year":
                node = BuildDateYearNode(jsProp, rawConst, operation);
                return true;

            case "Month":
                node = BuildComponentCondition(jsProp, rawConst, operation, "Month");
                return true;

            case "Day":
                node = BuildComponentCondition(jsProp, rawConst, operation, "Day");
                return true;

            case "DayOfYear":
                node = BuildComponentCondition(jsProp, rawConst, operation, "DayOfYear");
                return true;

            case "DayOfWeek":
                node = BuildDayOfWeekNode(jsProp, bin.Right, operation);
                return true;

            default:
                return false;
        }
    }

    private FilterNode BuildDateYearNode(string jsProp, object value, string operation)
    {
        if (value is not int year)
            throw new InvalidOperationException("Expected integer constant for .Year comparison");

        string finalOp = operation switch
        {
            "Equal" => "YearEqual",
            "NotEqual" => "NotYearEqual",
            "GreaterThan" => "YearGreaterThan",
            "GreaterThanOrEqual" => "YearGreaterThanOrEqual",
            "LessThan" => "YearLessThan",
            "LessThanOrEqual" => "YearLessThanOrEqual",
            _ => throw new InvalidOperationException($"Unsupported operator '{operation}' for .Year")
        };

        return new FilterNode
        {
            NodeType = FilterNodeType.Condition,
            Condition = new FilterCondition(
                jsProp,
                finalOp,
                year,
                false,
                false
            )
        };
    }

    private static bool IsDateType(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        return actual == typeof(DateTime) || actual == typeof(DateOnly);
    }

    private FilterNode BuildDateEqualityRange(string jsProp, object rawConst, string op)
    {
        if (rawConst is not DateTime dt)
            throw new InvalidOperationException("Expected DateTime constant for .Date comparison");

        DateTime startOfDay = dt.Date;
        DateTime nextDay = startOfDay.AddDays(1);

        if (op is "Equal")
        {
            return new FilterNode
            {
                NodeType = FilterNodeType.Logical,
                Operator = FilterLogicalOperator.And,
                Children = new List<FilterNode>
                {
                    new FilterNode
                    {
                        NodeType = FilterNodeType.Condition,
                        Condition = new FilterCondition(jsProp, "GreaterThanOrEqual", startOfDay, false, false)
                    },
                    new FilterNode
                    {
                        NodeType = FilterNodeType.Condition,
                        Condition = new FilterCondition(jsProp, "LessThan", nextDay, false, false)
                    }
                }
            };
        }

        // For <, <=, >, >=, NotEqual, etc.
        return new FilterNode
        {
            NodeType = FilterNodeType.Condition,
            Condition = new FilterCondition(jsProp, op, startOfDay, false, false)
        };
    }


    private FilterNode BuildComponentCondition(string jsProp, object value, string operation, string component)
    {
        string finalOp = operation switch
        {
            "Equal" => $"{component}Equal",
            "NotEqual" => $"Not{component}Equal",
            "GreaterThan" => $"{component}GreaterThan",
            "GreaterThanOrEqual" => $"{component}GreaterThanOrEqual",
            "LessThan" => $"{component}LessThan",
            "LessThanOrEqual" => $"{component}LessThanOrEqual",
            _ => throw new InvalidOperationException($"Unsupported operator '{operation}' for .{component}")
        };

        return new FilterNode
        {
            NodeType = FilterNodeType.Condition,
            Condition = new FilterCondition(jsProp, finalOp, value, false, false)
        };
    }


    private FilterNode BuildDayOfWeekNode(string jsProp, Expression expr, string operation)
    {
        // Unwrap the Convert to get the raw int expression (already a DayOfWeek enum converted to int)
        Expression cleanExpr = UnwrapConvert(expr);

        object? result = Expression.Lambda(cleanExpr).Compile().DynamicInvoke();

        if (result is not int jsDayOfWeek)
            throw new InvalidOperationException("Expected integer value for .DayOfWeek comparison");

        return BuildComponentCondition(jsProp, jsDayOfWeek, operation, "DayOfWeek");
    }


    private static Expression UnwrapConvert(Expression expr)
    {
        while (expr.NodeType == ExpressionType.Convert || expr.NodeType == ExpressionType.ConvertChecked)
        {
            expr = ((UnaryExpression)expr).Operand;
        }
        return expr;
    }

    private List<string>? GetMemberAccessPath(Expression expr)
    {
        var path = new List<string>();
        while (expr is MemberExpression memberExpr)
        {
            path.Insert(0, memberExpr.Member.Name);
            expr = memberExpr.Expression!;
        }

        return expr is ParameterExpression ? path : null;
    }

    // ------------------------------
    // "Contains" flattening logic:
    // ------------------------------
    private bool TryFlattenContains(MethodCallExpression call, out IEnumerable<FilterCondition> flattened)
    {
        flattened = Enumerable.Empty<FilterCondition>();

        if (call.Method.Name != "Contains")
            return false;

        if (call.Method.DeclaringType == typeof(string))
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
        return name.StartsWith("TypeOf")
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
        expr = StripConvert(expr); // <-- handle Convert wrappers

        return expr switch
        {
            ConstantExpression c => c,

            // e.g., new DateTime(...) or anything not marked constant but compile-safe
            NewExpression or MemberExpression or MethodCallExpression =>
                Expression.Constant(Expression.Lambda(expr).Compile().DynamicInvoke()),

            _ => throw new InvalidOperationException($"Unsupported or non-constant expression: {expr}")
        };
    }

    private static Expression StripConvert(Expression expr)
    {
        while (expr is UnaryExpression unary && expr.NodeType == ExpressionType.Convert)
        {
            expr = unary.Operand;
        }
        return expr;
    }
}