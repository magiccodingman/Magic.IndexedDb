using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.LinqTranslation.Models;
using Magic.IndexedDb.Models;

namespace Magic.IndexedDb;

/// <summary>Builds deferred projections of mapped properties from a Magic query.</summary>
public static class MagicProjectionExtensions
{
    public static MagicProjectedQuery<TResult> Select<T, TResult>(
        this IMagicExecute<T> source, Expression<Func<T, TResult>> selector) where T : class
    {
        ArgumentNullException.ThrowIfNull(selector);
        var query = GetQuery(source);
        var row = Expression.Parameter(typeof(JsonElement), "row");
        var projection = new ProjectedMembers(selector.Parameters[0], row);
        var body = projection.Visit(selector.Body);
        if (projection.Names.Count == 0)
            throw new NotSupportedException("Select must reference at least one mapped property.");

        var materialize = Expression.Lambda<Func<JsonElement, TResult>>(body, row).Compile();
        var fields = projection.Names.ToArray();
        return new MagicProjectedQuery<TResult>(
            token => query.Manager.ProjectAsync(query, fields, token),
            token => query.Manager.ProjectStreamAsync(query, fields, token),
            materialize);
    }

    internal static MagicQuery<T> GetQuery<T>(IMagicExecute<T> source) where T : class => source switch
    {
        MagicQuery<T> direct => direct,
        LinqTranslation.Extensions.MagicQueryExtensions<T> staged => staged.MagicQuery,
        MagicCursor<T> cursor => cursor.MagicQuery,
        LinqTranslation.Extensions.MagicCursorExtension<T> stagedCursor => stagedCursor.MagicQuery,
        _ => throw new ArgumentException("A Magic table query is required.", nameof(source))
    };

    private sealed class ProjectedMembers(ParameterExpression source, ParameterExpression row) : ExpressionVisitor
    {
        private static readonly MethodInfo ReadMethod = typeof(ProjectedMembers)
            .GetMethod(nameof(Read), BindingFlags.Static | BindingFlags.NonPublic)!;
        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        public override Expression? Visit(Expression? node)
        {
            if (node is null) return null;
            if (node.NodeType is not (ExpressionType.MemberAccess or ExpressionType.New or ExpressionType.MemberInit))
                throw new NotSupportedException("Select supports direct mapped properties and objects initialized from those properties.");

            return base.Visit(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != source || node.Member is not PropertyInfo property)
                throw new NotSupportedException("Select requires direct mapped properties of the table model.");

            var entry = PropertyMappingCache.GetPropertyEntry(property, source.Type);
            if (entry.NotMapped)
                throw new NotSupportedException($"Select cannot project the unmapped property '{property.Name}'.");

            var name = entry.JsPropertyName;
            Names.Add(name);
            return Expression.Call(ReadMethod.MakeGenericMethod(property.PropertyType),
                row, Expression.Constant(name), Expression.Constant(property, typeof(PropertyInfo)));
        }

        protected override MemberBinding VisitMemberBinding(MemberBinding node)
        {
            if (node is not MemberAssignment)
                throw new NotSupportedException("Select supports direct object member assignments.");

            return base.VisitMemberBinding(node);
        }

        private static TValue? Read<TValue>(JsonElement row, string name, PropertyInfo property)
        {
            if (!row.TryGetProperty(name, out var value))
                return default;

            return value.Deserialize<TValue>(ReadOptions<TValue>.ByProperty.GetOrAdd(property, static member =>
            {
                var settings = new MagicJsonSerializationSettings { UseCamelCase = true };
                return MagicConverterHelper.ForProperty(member, settings.GetOptionsWithResolver<TValue>());
            }));
        }
        private static class ReadOptions<TValue>
        {
            internal static readonly ConcurrentDictionary<PropertyInfo, JsonSerializerOptions> ByProperty = new();
        }
    }
}
