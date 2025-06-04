using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models;

internal class InternalMagicCompoundIndex<T> : IMagicCompoundIndex
{
    public string[] ColumnNamesInCompoundIndex { get; }
    public PropertyInfo[] PropertyInfos { get; }

    private InternalMagicCompoundIndex(params Expression<Func<T, object>>[] properties)
    {
        if (properties == null || properties.Length < 2)
        {
            throw new ArgumentException("Compound indexes require at least 2 properties.", nameof(properties));
        }

        // 🔥 Step 1: Retrieve PropertyInfos
        PropertyInfos = properties
            .Select(GetPropertyInfo)
            .ToArray();

        // 🔥 Step 2: Retrieve JS property names **without causing infinite recursion**
        ColumnNamesInCompoundIndex = PropertyInfos
            .Select(x => PropertyMappingCache.GetJsPropertyNameNoCache(
                PropertyMappingCache.GetPropertyColumnAttribute(x), x.Name)) // ✅ Correct JS name retrieval
            .ToArray();

        // 🔥 Step 3: Validate the compound index properties
        ValidateIndexes();
    }

    internal static IMagicCompoundIndex Create(params Expression<Func<T, object>>[] keySelectors)
    {
        return new InternalMagicCompoundIndex<T>(keySelectors);
    }

    private void ValidateIndexes()
    {
        if (ColumnNamesInCompoundIndex.Distinct().Count() != ColumnNamesInCompoundIndex.Length)
        {
            throw new InvalidOperationException(
                $"Duplicate properties detected in the compound index for type '{typeof(T).Name}'. Each property must be unique.");
        }
    }

    /// <summary>
    /// Extracts PropertyInfo from the provided expression.
    /// </summary>
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

    /// <summary>
    /// Ensures the property exists and is valid for indexing.
    /// </summary>
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
                $"Cannot use nested properties like '{memberExpr.Member.Name}' in a Compound Index for type '{typeof(T).Name}'. " +
                "Only top-level properties can be indexed.");
        }

        if (property.GetCustomAttribute<MagicNotMappedAttribute>() != null)
        {
            throw new InvalidOperationException(
                $"Cannot use the non-mapped property '{property.Name}' in a Compound Index for type '{typeof(T).Name}'.");
        }

        return property;
    }
}