using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    internal class InternalMagicCompoundKey<T> : IMagicCompoundKey
    {
        public string[] ColumnNamesInCompoundKey { get; }

        private InternalMagicCompoundKey(params Expression<Func<T, object>>[] properties)
        {
            if (properties == null || properties.Length < 2)
            {
                throw new ArgumentException("Compound keys require at least 2 properties.", nameof(properties));
            }

            ColumnNamesInCompoundKey = properties
                .Select(GetPropertyName)
                .ToArray();
        }

        internal static IMagicCompoundKey Create(params Expression<Func<T, object>>[] keySelectors)
        {
            return new InternalMagicCompoundKey<T>(keySelectors);
        }

        private static string GetPropertyName(Expression<Func<T, object>> propertyExpression)
        {
            if (propertyExpression.Body is MemberExpression memberExpr)
            {
                return ValidateAndGetPropertyName(memberExpr);
            }
            else if (propertyExpression.Body is UnaryExpression unaryExpr && unaryExpr.Operand is MemberExpression unaryMemberExpr)
            {
                return ValidateAndGetPropertyName(unaryMemberExpr);
            }
            else
            {
                throw new ArgumentException("Invalid expression format. Use property selectors like: `x => x.PropertyName`.");
            }
        }

        private static string ValidateAndGetPropertyName(MemberExpression memberExpr)
        {
            var property = typeof(T).GetProperty(memberExpr.Member.Name);
            if (property == null)
            {
                throw new ArgumentException($"Property '{memberExpr.Member.Name}' does not exist on type '{typeof(T).Name}'.");
            }

            if (property.GetCustomAttribute<MagicNotMappedAttribute>() != null)
            {
                throw new InvalidOperationException(
                    $"Cannot use the non-mapped property '{property.Name}' in a compound key on type '{typeof(T).Name}'.");
            }

            if (property.GetCustomAttribute<MagicPrimaryKeyAttribute>() != null)
            {
                throw new InvalidOperationException(
                    $"Cannot use the primary key property '{property.Name}' in a compound key on type '{typeof(T).Name}'.");
            }

            return PropertyMappingCache.GetJsPropertyName<T>(property);
        }
    }
}
