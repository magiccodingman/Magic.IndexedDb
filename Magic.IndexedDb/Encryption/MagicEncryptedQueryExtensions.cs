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

    /// <summary>Replaces a protected record if its revision and the workspace generation still match.</summary>
    public static Task<MagicConditionalMutationResult> ReplaceProtectedIfCurrentAsync<T, TWorkspace, TRevision, TGeneration>(
        this IMagicQuery<T> source, T expectedRecord, T replacement, Expression<Func<T, TRevision>> revision,
        IMagicQuery<TWorkspace> workspace, TWorkspace expectedWorkspace, Expression<Func<TWorkspace, TGeneration>> generation,
        CancellationToken cancellationToken = default) where T : class where TWorkspace : class
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return MutateIfCurrent(source, expectedRecord, replacement, revision, workspace, expectedWorkspace, generation, cancellationToken);
    }

    /// <summary>Deletes a protected record if its revision and the workspace generation still match.</summary>
    public static Task<MagicConditionalMutationResult> DeleteProtectedIfCurrentAsync<T, TWorkspace, TRevision, TGeneration>(
        this IMagicQuery<T> source, T expectedRecord, Expression<Func<T, TRevision>> revision,
        IMagicQuery<TWorkspace> workspace, TWorkspace expectedWorkspace, Expression<Func<TWorkspace, TGeneration>> generation,
        CancellationToken cancellationToken = default) where T : class where TWorkspace : class =>
        MutateIfCurrent(source, expectedRecord, null, revision, workspace, expectedWorkspace, generation, cancellationToken);

    /// <summary>Inserts a protected record with an unused key if the workspace generation still matches.</summary>
    public static Task<MagicConditionalMutationResult> InsertProtectedIfCurrentAsync<T, TWorkspace, TGeneration>(
        this IMagicQuery<T> source, T record, IMagicQuery<TWorkspace> workspace, TWorkspace expectedWorkspace,
        Expression<Func<TWorkspace, TGeneration>> generation, CancellationToken cancellationToken = default)
        where T : class where TWorkspace : class =>
        MutateIfCurrent<T, TWorkspace, object, TGeneration>(source, record, record, null, workspace, expectedWorkspace,
            generation, cancellationToken);

    private static async Task<MagicConditionalMutationResult> MutateIfCurrent<T, TWorkspace, TRevision, TGeneration>(
        IMagicQuery<T> source, T expectedRecord, T? replacement, Expression<Func<T, TRevision>>? revision,
        IMagicQuery<TWorkspace> workspace, TWorkspace expectedWorkspace, Expression<Func<TWorkspace, TGeneration>> generation,
        CancellationToken token) where T : class where TWorkspace : class
    {
        if (source is not MagicQuery<T> query || workspace is not MagicQuery<TWorkspace> workspaceQuery ||
            query.Manager != workspaceQuery.Manager || query.DatabaseName != workspaceQuery.DatabaseName)
            throw new ArgumentException("Both tables must belong to the same managed database context.");
        ArgumentNullException.ThrowIfNull(expectedRecord);
        ArgumentNullException.ThrowIfNull(expectedWorkspace);
        var settings = query.Manager.Settings(query.DatabaseName);
        var record = MagicSerializationHelper.SerializeObjectToJsonElement(expectedRecord, settings);
        var workspaceRecord = MagicSerializationHelper.SerializeObjectToJsonElement(expectedWorkspace, settings);
        var revisionName = revision is null ? null : MappedMember(revision).JsPropertyName;
        var generationName = MappedMember(generation).JsPropertyName;
        var next = replacement is null ? (JsonElement?)null : MagicSerializationHelper.SerializeObjectToJsonElement(replacement, settings);
        var applied = await query.Manager.ConditionalProtectedMutationAsync(query, workspaceQuery.SchemaName,
            KeyOf(expectedRecord, record), revisionName, revisionName is null ? null : record.GetProperty(revisionName),
            KeyOf(expectedWorkspace, workspaceRecord), generationName, workspaceRecord.GetProperty(generationName), next, token);
        return applied ? MagicConditionalMutationResult.Applied : MagicConditionalMutationResult.Conflict;
    }

    private static System.Text.Json.Nodes.JsonObject KeyOf<T>(T item, JsonElement serialized) where T : class
    {
        var key = new System.Text.Json.Nodes.JsonObject();
        foreach (var part in AttributeHelpers.GetPrimaryKeys(item))
            key[part.JsName] = System.Text.Json.Nodes.JsonNode.Parse(serialized.GetProperty(part.JsName).GetRawText());
        return key;
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

/// <summary>The outcome of an atomic conditional protected mutation.</summary>
public enum MagicConditionalMutationResult
{
    Conflict,
    Applied
}
