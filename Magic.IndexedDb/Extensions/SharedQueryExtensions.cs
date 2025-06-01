using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Extensions
{
    internal static class SharedQueryExtensions
    {
        internal static MagicQuery<T> Take<T>(MagicQuery<T> magicQuery, int amount)
        where T : class
        {
            var _MagicQuery = new MagicQuery<T>(magicQuery);
            var smq = new StoredMagicQuery
            {
                additionFunction = MagicQueryFunctions.Take,
                intValue = amount
            };

            _MagicQuery.StoredMagicQueries.Add(smq);
            return _MagicQuery;
        }

        public static MagicQuery<T> TakeLast<T>(MagicQuery<T> magicQuery, int amount) where T : class
        {
            var _MagicQuery = new MagicQuery<T>(magicQuery);
            _MagicQuery.StoredMagicQueries.Add(new StoredMagicQuery
            {
                additionFunction = MagicQueryFunctions.Take_Last,
                intValue = amount
            });
            return _MagicQuery;
        }

        public static MagicQuery<T> Skip<T>(MagicQuery<T> magicQuery, int amount) where T : class
        {
            var _MagicQuery = new MagicQuery<T>(magicQuery);
            _MagicQuery.StoredMagicQueries.Add(new StoredMagicQuery
            {
                additionFunction = MagicQueryFunctions.Skip,
                intValue = amount
            });
            return _MagicQuery;
        }

        public static MagicQuery<T> OrderBy<T>(MagicQuery<T> magicQuery, Expression<Func<T, object>> predicate) where T : class
        {
            var memberExpression = GetMemberExpressionFromLambda(predicate);
            var propertyInfo = memberExpression.Member as PropertyInfo;

            if (propertyInfo == null)
                throw new ArgumentException("The expression must represent a single property access.");

            MagicPropertyEntry mpe = PropertyMappingCache.GetPropertyEntry<T>(propertyInfo);

            if (!mpe.PrimaryKey && !mpe.Indexed && !mpe.UniqueIndex)
            {
                // Intentionally preserved your comment
                // throw new ArgumentException(...);
            }

            var _MagicQuery = new MagicQuery<T>(magicQuery);
            _MagicQuery.StoredMagicQueries.Add(new StoredMagicQuery
            {
                additionFunction = MagicQueryFunctions.Order_By,
                property = mpe.JsPropertyName
            });

            return _MagicQuery;
        }

        public static MagicQuery<T> OrderByDescending<T>(MagicQuery<T> magicQuery, Expression<Func<T, object>> predicate) where T : class
        {
            var memberExpression = GetMemberExpressionFromLambda(predicate);
            var propertyInfo = memberExpression.Member as PropertyInfo;

            if (propertyInfo == null)
                throw new ArgumentException("The expression must represent a single property access.");

            var _MagicQuery = new MagicQuery<T>(magicQuery);
            _MagicQuery.StoredMagicQueries.Add(new StoredMagicQuery
            {
                additionFunction = MagicQueryFunctions.Order_By_Descending,
                property = PropertyMappingCache.GetJsPropertyName<T>(propertyInfo)
            });

            return _MagicQuery;
        }

        private static MemberExpression GetMemberExpressionFromLambda<T>(Expression<Func<T, object>> expression)
        {
            if (expression.Body is MemberExpression m)
                return m;

            if (expression.Body is UnaryExpression u && u.Operand is MemberExpression um)
                return um;

            throw new ArgumentException("The expression must represent a single property access.");
        }
    }
}
