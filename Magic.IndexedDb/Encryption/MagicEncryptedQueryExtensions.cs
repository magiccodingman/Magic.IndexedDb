using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.LinqTranslation.Extensions;
using Magic.IndexedDb.LinqTranslation.Models;
using Magic.IndexedDb.Models;

namespace Magic.IndexedDb;

/// <summary>Accesses explicitly public members of a protected table while locked.</summary>
public static class MagicEncryptedQueryExtensions
{
    public static async Task<IReadOnlyList<TValue?>> SelectPublicAsync<T, TValue>(
        this IMagicExecute<T> source, Expression<Func<T, TValue>> member, CancellationToken cancellationToken = default)
        where T : class
    {
        var query = source switch
        {
            MagicQuery<T> direct => direct,
            MagicQueryExtensions<T> staged => staged.MagicQuery,
            MagicCursor<T> cursor => cursor.MagicQuery,
            MagicCursorExtension<T> stagedCursor => stagedCursor.MagicQuery,
            _ => throw new ArgumentException("A Magic table query is required.", nameof(source))
        };
        var property = MappedMember(member);
        string name = property.JsPropertyName;
        var predicate = new MagicQueryExtensions<T>(query).GetCollectedBinaryJsonExpressions();
        var rows = await query.Manager.ProjectPublicAsync(query, predicate, name, cancellationToken);
        var settings = query.Manager.Settings(query.DatabaseName);
        var nativeKey = settings.NumericStorageMode == MagicNumericStorageMode.Lossless &&
            MagicNumericSerialization.IsNativeAutoIncrementKey(property.Property, typeof(T));
        var options = MagicConverterHelper.ForProperty(property.Property,
            nativeKey ? settings.Options : settings.GetOptionsWithNumericConverters());
        return rows.Select(row => row.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? (TValue?)ReadValue(value)
            : default).ToArray();

        object? ReadValue(JsonElement value)
        {
            if (!nativeKey && !MagicConverterHelper.HasCustomConverter(property.Property, options))
                return MagicSerializationHelper.DeserializeObject<TValue>(value.GetRawText(), settings);
            var result = JsonSerializer.Deserialize(value.GetRawText(), property.Property.PropertyType, options);
            return nativeKey ? MagicNumericSerialization.ValidateNativeKey(result, property.Property.PropertyType) : result;
        }
    }

    /// <summary>Updates one explicitly public member without replacing the record or its ciphertext.</summary>
    public static Task<int> PatchPublicAsync<T, TValue>(
        this IMagicQuery<T> source, T recordWithKey, Expression<Func<T, TValue>> member,
        CancellationToken cancellationToken = default) where T : class
    {
        if (source is not MagicQuery<T> query)
            throw new ArgumentException("A Magic table query is required.", nameof(source));
        ArgumentNullException.ThrowIfNull(recordWithKey);
        string name = MappedMember(member).JsPropertyName;
        var settings = query.Manager.Settings(query.DatabaseName);
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

    private static MagicPropertyEntry MappedMember<T, TValue>(Expression<Func<T, TValue>> member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (member.Body is not MemberExpression { Member: PropertyInfo property } access ||
            access.Expression != member.Parameters[0])
            throw new ArgumentException("Select one direct persisted property.", nameof(member));

        var entry = PropertyMappingCache.GetPropertyEntry(property, typeof(T));
        if (entry.NotMapped)
            throw new ArgumentException("Select a persisted property.", nameof(member));

        return entry;
    }
}
