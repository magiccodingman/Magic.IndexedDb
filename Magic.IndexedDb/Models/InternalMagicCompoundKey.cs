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
    internal class InternalMagicCompoundKey<T> : IMagicCompoundKey
    {
        public string[] ColumnNamesInCompoundKey { get; }
        public PropertyInfo[] PropertyInfos { get; }
        public bool AutoIncrement { get; }
        public bool IsCompoundKey { get; } // New flag to track if it's a compound key

        private InternalMagicCompoundKey(bool autoIncrement, params Expression<Func<T, object>>[] properties)
        {
            AutoIncrement = autoIncrement;

            PropertyInfos = properties
                .Select(GetPropertyInfo)
                .ToArray();

            ColumnNamesInCompoundKey = PropertyInfos
                .Select(x => PropertyMappingCache.GetJsPropertyNameNoCache(
                PropertyMappingCache.GetPropertyColumnAttribute(x)
                , x.Name))
                .ToArray();

            IsCompoundKey = PropertyInfos.Length > 1; // If more than one key, it's a compound key

            ValidateKeys();
        }

        internal static IMagicCompoundKey Create(bool autoIncrement, params Expression<Func<T, object>>[] keySelectors)
        {
            return new InternalMagicCompoundKey<T>(autoIncrement, keySelectors);
        }

        private void ValidateKeys()
        {
            string keyType = IsCompoundKey ? "Compound Key" : "Primary Key";

            // **Check for duplicate column names**
            if (ColumnNamesInCompoundKey.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ColumnNamesInCompoundKey.Length)
            {
                throw new InvalidOperationException(
                    $"Duplicate properties detected in the {keyType} for type '{typeof(T).Name}'. Each property must be unique.");
            }

            // **Prevent 'id' (case-insensitive) from being in compound keys**
            if (IsCompoundKey && ColumnNamesInCompoundKey.Any(col => col.Equals("id", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Invalid Compound Key in '{typeof(T).Name}': The column name 'id' cannot be part of a Compound Key. " +
                    "IndexedDB does not allow an explicit 'id' field when using compound keys.");
            }

            // **AutoIncrement should not be allowed for compound keys**
            if (IsCompoundKey && AutoIncrement)
            {
                throw new InvalidOperationException(
                    $"Cannot mark a Compound Key as AutoIncrement for type '{typeof(T).Name}'. " +
                    "AutoIncrement is only allowed on single Primary Keys.");
            }
        }


        private static PropertyInfo GetPropertyInfo(Expression<Func<T, object>> propertyExpression)
        {
            if (propertyExpression.Body is MemberExpression memberExpr)
            {
                return ValidateAndGetPropertyInfo(memberExpr);
            }
            else if (propertyExpression.Body is UnaryExpression unaryExpr && unaryExpr.Operand is MemberExpression unaryMemberExpr)
            {
                return ValidateAndGetPropertyInfo(unaryMemberExpr);
            }
            else
            {
                throw new ArgumentException("Invalid expression format. Use property selectors like: `x => x.PropertyName`.");
            }
        }

        private static PropertyInfo ValidateAndGetPropertyInfo(MemberExpression memberExpr)
        {
            var property = typeof(T).GetProperty(memberExpr.Member.Name);
            if (property == null)
            {
                throw new ArgumentException($"Property '{memberExpr.Member.Name}' does not exist on type '{typeof(T).Name}'.");
            }

            if (memberExpr.Expression is MemberExpression)
            {
                throw new InvalidOperationException(
                    $"Cannot use nested properties like '{memberExpr.Member.Name}' in a Primary or Compound Key for type '{typeof(T).Name}'. " +
                    "Only top-level properties can be indexed.");
            }

            if (property.GetCustomAttribute<MagicNotMappedAttribute>() != null)
            {
                throw new InvalidOperationException(
                    $"Cannot use the non-mapped property '{property.Name}' in a Primary or Compound Key for type '{typeof(T).Name}'.");
            }

            if (property.GetCustomAttribute<MagicUniqueIndexAttribute>() != null)
            {
                throw new InvalidOperationException(
                    $"Cannot define a Primary or Compound Key including '{property.Name}' because it is already marked as a unique index " +
                    $"on type '{typeof(T).Name}'. Either remove the unique index or exclude it from the key.");
            }

            return property;
        }
    }
}
