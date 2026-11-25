using System.Text;
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
        var settings = query.Manager.Settings(query.DatabaseName);
        if (MagicConverterHelper.HasCustomConverter(typeof(T), settings.Options))
            throw new NotSupportedException("Select cannot project a table whose converter defines its entire JSON representation.");

        var row = Expression.Parameter(typeof(JsonElement), "row");
        var projection = new ProjectedMembers<T>(selector.Parameters[0], row, settings);
        var body = projection.Visit(selector.Body);
        if (projection.Names.Count == 0)
            throw new NotSupportedException("Select must reference at least one mapped property.");

        var materialize = Expression.Lambda<Func<JsonElement, TResult>>(body, row).Compile(preferInterpretation: true);
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

    private sealed class ProjectedMembers<T>(ParameterExpression source, ParameterExpression row,
        MagicJsonSerializationSettings settings) : ExpressionVisitor where T : class
    {
        private static readonly MethodInfo ReadMethod =
            ((MethodCallExpression)((Expression<Func<ProjectedMembers<T>, JsonElement, MagicPropertyEntry, object?>>)
                ((projector, value, member) => projector.Read(value, member))).Body).Method;
        private readonly JsonSerializerOptions _options = settings.GetOptionsWithResolver<T>();
        private readonly MagicContractResolver<T> _resolver = new();
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

            var entry = PropertyMappingCache.GetPropertyEntry(property, typeof(T));
            if (entry.NotMapped || (!property.CanWrite && !PropertyMappingCache.GetTypeOfTProperties(typeof(T))
                    .ConstructorParameterMappings.ContainsKey(property.Name)))
                throw new NotSupportedException($"Select cannot project the unmapped property '{property.Name}'.");

            Names.Add(entry.JsPropertyName);
            return Expression.Convert(Expression.Call(Expression.Constant(this), ReadMethod,
                row, Expression.Constant(entry)), property.PropertyType);
        }

        protected override MemberBinding VisitMemberBinding(MemberBinding node)
        {
            if (node is not MemberAssignment)
                throw new NotSupportedException("Select supports direct object member assignments.");
            return base.VisitMemberBinding(node);
        }

        private object? Read(JsonElement row, MagicPropertyEntry property)
        {
            if (!row.TryGetProperty(property.JsPropertyName, out var value))
                throw new JsonException($"Selected property '{property.JsPropertyName}' is missing from the stored record.");

            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(value.GetRawText()));
            reader.Read();
            var result = _resolver.ReadMappedProperty(ref reader, property, typeof(T), _options);
            if (result is null && property.Property.PropertyType.IsValueType &&
                Nullable.GetUnderlyingType(property.Property.PropertyType) is null)
                throw new JsonException($"Selected property '{property.JsPropertyName}' is null but its type is non-nullable.");
            return result;
        }
    }
}
