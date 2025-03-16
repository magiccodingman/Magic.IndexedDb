using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models
{
    internal class InternalMagicCompoundIndex<T> : IMagicCompoundIndex
    {
        public string[] ColumnNamesInCompoundIndex { get; }

        private InternalMagicCompoundIndex(params Expression<Func<T, object>>[] properties)
        {
            if (properties == null || properties.Length < 2)
            {
                throw new ArgumentException("Compound keys require at least 2 properties.", nameof(properties));
            }

            ColumnNamesInCompoundIndex = properties
                .Select(GetPropertyName)
                .ToArray();

            if (ColumnNamesInCompoundIndex.Distinct().Count() != ColumnNamesInCompoundIndex.Length)
            {
                throw new InvalidOperationException(
                    $"Duplicate properties detected in the compound index for type '{typeof(T).Name}'. Each property must be unique.");
            }
        }

        internal static IMagicCompoundIndex Create(params Expression<Func<T, object>>[] keySelectors)
        {
            return new InternalMagicCompoundIndex<T>(keySelectors);
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

            if (memberExpr.Expression is MemberExpression nestedExpr)
            {
                throw new InvalidOperationException(
                    $"Cannot compound index nested properties like '{memberExpr.Member.Name}' in compound keys or indexes on type '{typeof(T).Name}'. " +
                    "Only top-level properties can be indexed.");
            }

            if (property.GetCustomAttribute<MagicNotMappedAttribute>() != null)
            {
                throw new InvalidOperationException(
                    $"Cannot use the non-mapped property '{property.Name}' in a compound key on type '{typeof(T).Name}'.");
            }

            return PropertyMappingCache.GetJsPropertyName<T>(property);
        }
    }
}
