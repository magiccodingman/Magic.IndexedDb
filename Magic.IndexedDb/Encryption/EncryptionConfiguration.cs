using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.SchemaAnnotations;

namespace Magic.IndexedDb;

internal sealed class EncryptionConfiguration
{
    private static readonly ConcurrentDictionary<Type, string[]> Fields = new();
    private readonly Dictionary<string, string[]> _recordPolicies;
    public MagicEncryptionKeyRequest Request { get; }
    public string PhysicalName { get; }
    public string Route { get; }

    public EncryptionConfiguration(MagicEncryptionOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AccountScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.KeyId);
        if (options.DatabaseName.StartsWith("__magic_protected__:", StringComparison.Ordinal))
            throw new ArgumentException("The logical database name uses a reserved prefix.", nameof(options));

        Request = new(options.DatabaseName, options.AccountScope, options.KeyId);
        var identity = JsonSerializer.SerializeToUtf8Bytes(new[] { options.DatabaseName, options.AccountScope });
        PhysicalName = $"__magic_protected__:{Convert.ToHexString(SHA256.HashData(identity))}";
        Route = $"{PhysicalName}:{Guid.NewGuid():N}";
        _recordPolicies = options.RecordPolicies?.ToDictionary(
            pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
    }

    public static string[] ProtectedFields(Type type) => Fields.GetOrAdd(type, static table =>
        table.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(property => property.IsDefined(typeof(MagicEncryptAttribute), inherit: true))
            .Select(property =>
            {
                var entry = PropertyMappingCache.GetPropertyEntry(property, table);
                if (entry.NotMapped || property.GetIndexParameters().Length != 0)
                    throw new InvalidOperationException($"Encrypted member '{table.Name}.{property.Name}' must be a persisted property.");
                return entry.JsPropertyName;
            }).ToArray());

    public static bool RequiresProtection(Type type) =>
        type.IsDefined(typeof(MagicEncryptRecordAttribute), inherit: true) || ProtectedFields(type).Length > 0;

    private string[]? GetExposedFields(Type table, string tableName)
    {
        _recordPolicies.TryGetValue(tableName, out var configured);
        var annotation = table.GetCustomAttribute<MagicEncryptRecordAttribute>(inherit: true);
        if (configured is null && annotation is null)
            return null;

        var mapped = PropertyMappingCache.GetTypeOfTProperties(table).propertyEntries.Values
            .Where(entry => !entry.NotMapped && entry.Property.GetIndexParameters().Length == 0).ToArray();
        var annotated = annotation?.ExposedProperties.Select(property => mapped
            .SingleOrDefault(entry => entry.CsharpPropertyName == property).Property is { } member
                ? PropertyMappingCache.GetJsPropertyName(member, table)
                : throw new InvalidOperationException($"Magic record policy for '{tableName}' names an unknown CLR property."))
            .ToArray();
        if (configured is not null && configured.Any(name => !mapped.Any(entry => entry.JsPropertyName == name)))
            throw new InvalidOperationException($"Magic record policy for '{tableName}' exposes an unknown persisted property.");
        if (annotated is not null && configured is not null &&
            !annotated.ToHashSet(StringComparer.Ordinal).SetEquals(configured))
            throw new InvalidOperationException($"Magic record policy for '{tableName}' conflicts with its model annotation.");

        return annotated ?? configured;
    }

    public JsonElement CreateDatabase(IEnumerable<(Type Type, StoreSchema Schema)> schemas)
    {
        var rows = schemas.Select(entry =>
        {
            var protectedFields = ProtectedFields(entry.Type);
            var exposedFields = GetExposedFields(entry.Type, entry.Schema.TableName);
            if (exposedFields is null && protectedFields.Length > 0 &&
                MagicConverterHelper.HasCustomConverter(entry.Type, new JsonSerializerOptions()))
            {
                // A whole-table converter can rename or duplicate protected
                // values outside their annotated fields. Only a complete record
                // payload can safely protect an otherwise arbitrary wire shape.
                throw new InvalidOperationException(
                    $"Table '{entry.Schema.TableName}' uses a custom JSON converter and cannot use field encryption. " +
                    "Configure whole-record encryption with an explicit public allowlist instead.");
            }

            return new
            {
                entry.Schema.TableName,
                entry.Schema.PrimaryKeyAuto,
                entry.Schema.ColumnNamesInCompoundKey,
                entry.Schema.Indexes,
                entry.Schema.UniqueIndexes,
                entry.Schema.ColumnNamesInCompoundIndex,
                EncryptedFields = protectedFields,
                ExposedFields = exposedFields
            };
        }).ToArray();

        if (_recordPolicies.Keys.Any(table => !rows.Any(row => row.TableName == table)))
            throw new InvalidOperationException("A Magic record policy names an unregistered table.");

        return JsonSerializer.SerializeToElement(new
        {
            Name = Route,
            Version = 1,
            StoreSchemas = rows,
            Encryption = new { Account = Request.AccountScope, PhysicalName }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
