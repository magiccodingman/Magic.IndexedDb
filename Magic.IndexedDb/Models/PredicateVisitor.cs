using System.Collections;
using System.Linq.Expressions;

namespace Magic.IndexedDb.Models;

public class PredicateVisitor<T> : ExpressionVisitor
{
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == "Any" && node.Arguments[0] is MemberExpression member)
        {
            var lambda = GetLambdaExpression(node.Arguments[1]);
            var rewritten = GetIEnumerableItems(member)
                .Select(value => ReplaceParameter(lambda, value))
                .ToList();

            // Enumerable.Any over an empty sequence is false.
            return rewritten.Count == 0
                ? Expression.Constant(false)
                : rewritten.Aggregate<Expression>((left, right) => Expression.OrElse(left, right));
        }
        else if (node.Method.Name == "All" && node.Arguments[0] is MemberExpression member3)
        {
            var lambda = GetLambdaExpression(node.Arguments[1]);
            var rewritten = GetIEnumerableItems(member3)
                .Select(value => ReplaceParameter(lambda, value))
                .ToList();

            // Enumerable.All over an empty sequence is vacuously true.
            return rewritten.Count == 0
                ? Expression.Constant(true)
                : rewritten.Aggregate<Expression>((left, right) => Expression.AndAlso(left, right));
        }
        else
        {
            return base.VisitMethodCall(node);
        }
    }

    private LambdaExpression GetLambdaExpression(Expression expression)
    {
        if (expression is UnaryExpression unaryExpression)
        {
            if (unaryExpression.Operand is LambdaExpression lambdaExpression)
            {
                return lambdaExpression;
            }
        }
        else if (expression is LambdaExpression lambda)
        {
            return lambda;
        }

        throw new InvalidOperationException("Invalid expression type.");
    }

    private IEnumerable<object> GetIEnumerableItems(MemberExpression member)
    {
        var compiledMember = Expression.Lambda<Func<IEnumerable>>(member).Compile();
        var enumerable = compiledMember();
        return enumerable.OfType<object>();
    }

    private Expression ReplaceParameter(LambdaExpression lambda, object value)
    {
        var parameter = lambda.Parameters.FirstOrDefault();
        if (parameter != null)
        {
            var constant = Expression.Constant(value, parameter.Type);
            var body = new ParameterReplacer(parameter, constant).Visit(lambda.Body);
            return body;
        }
        else
        {
            return Expression.Empty();
        }
    }

    private class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _parameter;
        private readonly Expression _replacement;

        public ParameterReplacer(ParameterExpression parameter, Expression replacement)
        {
            _parameter = parameter;
            _replacement = replacement;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _parameter)
            {
                return _replacement;
            }

            return base.VisitParameter(node);
        }
    }
}