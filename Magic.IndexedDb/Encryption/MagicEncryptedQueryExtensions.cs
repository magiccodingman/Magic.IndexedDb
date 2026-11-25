using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.LinqTranslation.Extensions;
using Magic.IndexedDb.Models;

namespace Magic.IndexedDb;

/// <summary>Accesses explicitly public members of a protected table while locked.</summary>
public static class MagicEncryptedQueryExtensions
{
    public static async Task<IReadOnlyList<TValue?>> SelectPublicAsync<T, TValue>(
        this IMagicExecute<T> source, Expression<Func<T, TValue>> member, CancellationToken cancellationToken = default)
        where T : class
    {
        var query = MagicProjectionExtensions.GetQuery(source);
        string name = MappedMember(member);
        var predicate = new MagicQueryExtensions<T>(query).GetCollectedBinaryJsonExpressions();
        var rows = await query.Manager.ProjectPublicAsync(query, predicate, name, cancellationToken);
        var settings = new MagicJsonSerializationSettings { UseCamelCase = true };
        return rows.Select(row => row.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? MagicSerializationHelper.DeserializeObject<TValue>(value.GetRawText(), settings)
            : default).ToArray();
    }

    /// <summary>Updates one explicitly public member without replacing the record or its ciphertext.</summary>
    public static Task<int> PatchPublicAsync<T, TValue>(
        this IMagicQuery<T> source, T recordWithKey, Expression<Func<T, TValue>> member,
        CancellationToken cancellationToken = default) where T : class
    {
        if (source is not MagicQuery<T> query)
            throw new ArgumentException("A Magic table query is required.", nameof(source));
        ArgumentNullException.ThrowIfNull(recordWithKey);
        string name = MappedMember(member);
        var settings = new MagicJsonSerializationSettings { UseCamelCase = true };
        var serialized = MagicSerializationHelper.SerializeObjectToJsonElement(recordWithKey, settings);
        var keyParts = AttributeHelpers.GetPrimaryKeys(recordWithKey)
            .Select(part => serialized.GetProperty(part.JsName)).ToArray();
        var value = serialized.TryGetProperty(name, out var field)
            ? field
            : JsonSerializer.SerializeToElement<object?>(null);
        object key = keyParts.Length == 1 ? keyParts[0] : keyParts;
        return query.Manager.PatchPublicAsync(query, key,
            JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [name] = value }), cancellationToken);
    }

    private static string MappedMember<T, TValue>(Expression<Func<T, TValue>> member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (member.Body is not MemberExpression { Member: PropertyInfo property } access ||
            access.Expression != member.Parameters[0])
            throw new ArgumentException("Select one direct persisted property.", nameof(member));

        var entry = PropertyMappingCache.GetPropertyEntry(property, typeof(T));
        if (entry.NotMapped)
            throw new ArgumentException("Select a persisted property.", nameof(member));

        return entry.JsPropertyName;
    }
}
