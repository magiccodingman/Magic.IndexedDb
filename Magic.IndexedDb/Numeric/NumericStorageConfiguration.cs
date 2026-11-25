using System.Security.Cryptography;
using System.Text.Json;

namespace Magic.IndexedDb;

internal sealed class NumericStorageConfiguration
{
    internal const string PhysicalPrefix = "__magic_numeric_v1__:";
    internal IReadOnlyDictionary<string, MagicNumericStorageMode> Modes { get; }

    internal NumericStorageConfiguration(IEnumerable<MagicNumericStorageOptions> databases)
    {
        ArgumentNullException.ThrowIfNull(databases);
        var modes = new Dictionary<string, MagicNumericStorageMode>(StringComparer.Ordinal);
        foreach (var database in databases)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentException.ThrowIfNullOrWhiteSpace(database.DatabaseName);
            if (!Enum.IsDefined(database.Mode)) throw new ArgumentOutOfRangeException(nameof(databases), "Unknown numeric storage mode.");
            if (database.DatabaseName.StartsWith("__magic_", StringComparison.Ordinal))
                throw new ArgumentException("Logical database names cannot use Magic's reserved physical namespaces.", nameof(databases));
            if (!modes.TryAdd(database.DatabaseName, database.Mode))
                throw new ArgumentException($"Numeric storage was configured more than once for '{database.DatabaseName}'.", nameof(databases));
        }
        Modes = modes;
    }

    internal MagicNumericStorageMode Mode(string name) => Modes.GetValueOrDefault(name);

    internal static string PhysicalName(string logicalName) => PhysicalPrefix +
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { logicalName })));

    internal static object Policy => new { Version = 1, Mode = "lossless" };

    internal static JsonElement CreateDatabase(string logicalName, IEnumerable<StoreSchema> schemas) =>
        JsonSerializer.SerializeToElement(new
        {
            Name = PhysicalName(logicalName),
            Version = 1,
            StoreSchemas = schemas,
            NumericStorage = Policy,
            NumericLegacyName = logicalName
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
