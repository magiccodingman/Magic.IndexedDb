using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models;

public class PredicateVisitor<T> : ExpressionVisitor
{
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == "Any" && node.Arguments[0] is MemberExpression member)
        {
            // Handle Any expressions
            var lambda = GetLambdaExpression(node.Arguments[1]);
            var values = GetIEnumerableItems(member);
            return values.Select(value => ReplaceParameter(lambda, value)).Aggregate<Expression>((left, right) => Expression.OrElse(left, right));
        }
        else if (node.Method.Name == "All" && node.Arguments[0] is MemberExpression member3)
        {
            // Handle All expressions
            var lambda = GetLambdaExpression(node.Arguments[1]);
            var values = GetIEnumerableItems(member3);
            return values.Select(value => ReplaceParameter(lambda, value)).Aggregate<Expression>((left, right) => Expression.AndAlso(left, right));
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