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

    private string[]? GetExposedFields(Type table, string tableName)
    {
        if (!_recordPolicies.TryGetValue(tableName, out var exposed))
            return null;

        var mappedNames = PropertyMappingCache.GetTypeOfTProperties(table).propertyEntries.Values
            .Where(entry => !entry.NotMapped && entry.Property.GetIndexParameters().Length == 0)
            .Select(entry => entry.JsPropertyName).ToHashSet(StringComparer.Ordinal);
        if (exposed.Any(name => !mappedNames.Contains(name)))
            throw new InvalidOperationException($"Magic record policy for '{tableName}' exposes an unknown persisted property.");

        return exposed;
    }

    public JsonElement CreateDatabase(IEnumerable<(Type Type, StoreSchema Schema)> schemas)
    {
        var rows = schemas.Select(entry => new
        {
            entry.Schema.TableName,
            entry.Schema.PrimaryKeyAuto,
            entry.Schema.ColumnNamesInCompoundKey,
            entry.Schema.Indexes,
            entry.Schema.UniqueIndexes,
            entry.Schema.ColumnNamesInCompoundIndex,
            EncryptedFields = ProtectedFields(entry.Type),
            ExposedFields = GetExposedFields(entry.Type, entry.Schema.TableName)
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
