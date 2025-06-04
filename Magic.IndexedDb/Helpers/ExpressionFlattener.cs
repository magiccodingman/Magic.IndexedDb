using System.Linq.Expressions;

namespace Magic.IndexedDb.Helpers;

/// <summary>
/// IndexDB requires flattening complex nested OR statements as it's not supported by default. 
/// Flattening resolves this issue but without optimizations flattened methods will call 
/// large numbers of redundant queries without some help.
/// </summary>
public static class ExpressionFlattener
{
    public static Expression<Func<T, bool>> FlattenAndOptimize<T>(Expression<Func<T, bool>> expr)
    {
        // Step 1: Flatten OrElse structures
        var flattenedBody = FlattenOrElseRecursive(expr.Body);

        // Step 2: Optimize the expression (deduplicate and simplify)
        var optimizedBody = OptimizeExpression(flattenedBody);

        return Expression.Lambda<Func<T, bool>>(optimizedBody, expr.Parameters);
    }

    private static Expression FlattenOrElseRecursive(Expression expr)
    {
        if (expr is BinaryExpression binaryExpr)
        {
            if (binaryExpr.NodeType == ExpressionType.OrElse)
            {
                var terms = new HashSet<Expression>(new LogicalExpressionComparer());
                CollectOrTerms(binaryExpr, terms);
                return BuildOrChain(terms);
            }
            else if (binaryExpr.NodeType == ExpressionType.AndAlso)
            {
                var left = FlattenOrElseRecursive(binaryExpr.Left);
                var right = FlattenOrElseRecursive(binaryExpr.Right);

                if (ContainsOrElse(left) || ContainsOrElse(right))
                {
                    return DistributeAndOverOr(left, right);
                }
                return SimplifyAndChain(left, right);
            }
        }
        return expr;
    }

    /// <summary>
    /// Recursively collects all expressions in an OrElse chain.
    /// </summary>
    private static void CollectOrTerms(Expression expr, HashSet<Expression> terms)
    {
        if (expr is BinaryExpression be && be.NodeType == ExpressionType.OrElse)
        {
            CollectOrTerms(be.Left, terms);
            CollectOrTerms(be.Right, terms);
        }
        else
        {
            terms.Add(expr);
        }
    }

    /// <summary>
    /// Rebuilds an OrElse chain from a set of expressions.
    /// </summary>
    private static Expression BuildOrChain(IEnumerable<Expression> terms)
    {
        var termList = terms.Distinct(LogicalExpressionComparer.Instance).ToList();
        termList.Sort(LogicalExpressionComparer.Instance);
        return termList.Aggregate(Expression.OrElse);
    }

    private static Expression DistributeAndOverOr(Expression left, Expression right)
    {
        var leftTerms = new HashSet<Expression>(new LogicalExpressionComparer());
        var rightTerms = new HashSet<Expression>(new LogicalExpressionComparer());

        CollectOrTerms(left, leftTerms);
        CollectOrTerms(right, rightTerms);

        var newTerms = new HashSet<Expression>(new LogicalExpressionComparer());
        foreach (var l in leftTerms)
        {
            foreach (var r in rightTerms)
            {
                newTerms.Add(Expression.AndAlso(l, r));
            }
        }

        return BuildOrChain(newTerms);
    }

    private static bool ContainsOrElse(Expression expr)
    {
        return expr is BinaryExpression binaryExpr &&
               (binaryExpr.NodeType == ExpressionType.OrElse ||
                ContainsOrElse(binaryExpr.Left) ||
                ContainsOrElse(binaryExpr.Right));
    }

    /// <summary>
    /// Recursively collects all expressions in an AndAlso chain.
    /// </summary>
    private static void CollectAndTerms(Expression expr, HashSet<Expression> terms)
    {
        if (expr is BinaryExpression be && be.NodeType == ExpressionType.AndAlso)
        {
            CollectAndTerms(be.Left, terms);
            CollectAndTerms(be.Right, terms);
        }
        else
        {
            terms.Add(expr);
        }
    }

    /// <summary>
    /// Optimizes an expression by recursively flattening AndAlso/OrElse trees,
    /// deduplicating equivalent subexpressions, and short-circuiting redundant branches.
    /// </summary>
    private static Expression OptimizeExpression(Expression expr)
    {
        if (expr is BinaryExpression be)
        {
            // Recursively optimize children first.
            if (be.NodeType == ExpressionType.AndAlso)
            {
                var left = OptimizeExpression(be.Left);
                var right = OptimizeExpression(be.Right);

                // If both sides are identical, return one.
                if (LogicalExpressionComparer.Instance.Equals(left, right))
                    return left;

                // Flatten all AndAlso terms into a set.
                var andTerms = new HashSet<Expression>(LogicalExpressionComparer.Instance);
                CollectAndTerms(left, andTerms);
                CollectAndTerms(right, andTerms);

                // If after deduplication we only have one term, just return it.
                if (andTerms.Count == 1)
                    return andTerms.First();

                return BuildAndChain(andTerms);
            }
            else if (be.NodeType == ExpressionType.OrElse)
            {
                var left = OptimizeExpression(be.Left);
                var right = OptimizeExpression(be.Right);

                // If both sides are identical, return one.
                if (LogicalExpressionComparer.Instance.Equals(left, right))
                    return left;

                // Flatten all OrElse terms into a set.
                var orTerms = new HashSet<Expression>(LogicalExpressionComparer.Instance);
                CollectOrTerms(left, orTerms);
                CollectOrTerms(right, orTerms);

                if (orTerms.Count == 1)
                    return orTerms.First();

                return BuildOrChain(orTerms);
            }
        }
        return expr;
    }

    private static Expression SimplifyAndChain(Expression left, Expression right)
    {
        var andTerms = new HashSet<Expression>(new LogicalExpressionComparer()) { left, right };

        if (andTerms.Count == 1) return andTerms.First(); // A && A → A

        return BuildAndChain(andTerms);
    }

    /// <summary>
    /// Rebuilds an AndAlso chain from a set of expressions.
    /// </summary>
    private static Expression BuildAndChain(IEnumerable<Expression> terms)
    {
        var termList = terms.Distinct(LogicalExpressionComparer.Instance).ToList();
        termList.Sort(LogicalExpressionComparer.Instance);
        return termList.Aggregate(Expression.AndAlso);
    }


    /// <summary>
    /// A custom comparer that determines logical equivalence of expressions.
    /// Adjust or extend this to detect more cases if needed.
    /// </summary>
    private class LogicalExpressionComparer : IEqualityComparer<Expression>, IComparer<Expression>
    {
        public static readonly LogicalExpressionComparer Instance = new();

        public bool Equals(Expression? x, Expression? y) => Compare(x, y) is 0;

        public int GetHashCode(Expression obj) => obj.ToString().GetHashCode();

        public int Compare(Expression? x, Expression? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            if (x.NodeType != y.NodeType)
                return x.NodeType.CompareTo(y.NodeType);

            switch (x)
            {
                case ConstantExpression cx when y is ConstantExpression cy:
                    return Comparer<object>.Default.Compare(cx.Value, cy.Value);

                case MemberExpression mx when y is MemberExpression my:
                    return string.Compare(mx.Member.Name, my.Member.Name, StringComparison.Ordinal);

                case BinaryExpression bx when y is BinaryExpression by:
                    // For commutative operations, check both orders.
                    if (IsCommutative(bx.NodeType) &&
                        Equals(bx.Left, by.Right) &&
                        Equals(bx.Right, by.Left))
                    {
                        return 0;
                    }
                    int leftCompare = Compare(bx.Left, by.Left);
                    return leftCompare != 0 ? leftCompare : Compare(bx.Right, by.Right);

                case MethodCallExpression mx when y is MethodCallExpression my:
                    int methodCompare = string.Compare(mx.Method.Name, my.Method.Name, StringComparison.Ordinal);
                    if (methodCompare != 0) return methodCompare;
                    // Compare arguments sequentially.
                    for (int i = 0; i < Math.Min(mx.Arguments.Count, my.Arguments.Count); i++)
                    {
                        int argCompare = Compare(mx.Arguments[i], my.Arguments[i]);
                        if (argCompare != 0) return argCompare;
                    }
                    return mx.Arguments.Count.CompareTo(my.Arguments.Count);

                default:
                    return string.Compare(x.ToString(), y.ToString(), StringComparison.Ordinal);
            }
        }

        private static bool IsCommutative(ExpressionType type)
        {
            return type == ExpressionType.Equal ||
                   type == ExpressionType.NotEqual ||
                   type == ExpressionType.OrElse ||
                   type == ExpressionType.AndAlso;
        }
    }
}