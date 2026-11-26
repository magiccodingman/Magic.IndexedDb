using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Migrations;

namespace Magic.IndexedDb;

/// <summary>A logical schema declaration and its default physical database name.</summary>
/// <remarks>Fluent methods return new declarations. Keep the resulting declaration in your repository.</remarks>
public sealed class IndexedDbSet
{
    public string DatabaseName { get; }
    public string SchemaId { get; }
    public string? ManifestUrl { get; }
    public bool DiscoverDefaultTables { get; }
    public IReadOnlyList<Type> IncludedTypes { get; }
    public IReadOnlyList<Type> ExcludedTypes { get; }

    public IndexedDbSet(string databaseName)
        : this(databaseName, databaseName, null, true, [], [])
    {
    }

    private IndexedDbSet(string databaseName, string schemaId, string? manifestUrl,
        bool discoverDefaultTables, IEnumerable<Type> includes, IEnumerable<Type> excludes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        DatabaseName = databaseName;
        SchemaId = schemaId;
        ManifestUrl = manifestUrl;
        DiscoverDefaultTables = discoverDefaultTables;
        IncludedTypes = Array.AsReadOnly(includes.Distinct().ToArray());
        ExcludedTypes = Array.AsReadOnly(excludes.Distinct().ToArray());
    }

    /// <summary>Sets a stable portable identity independent of the physical database name.</summary>
    public IndexedDbSet WithSchemaId(string schemaId)
    {
        MagicSchemaResolver.ValidateSchemaId(schemaId);
        return Copy(schemaId: schemaId);
    }

    /// <summary>Pins the static migration manifest used by this application release.</summary>
    public IndexedDbSet WithMigrations(string manifestUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestUrl);
        if (manifestUrl.Any(char.IsControl) || manifestUrl.Contains('\\'))
            throw new ArgumentException("Migration manifest URLs cannot contain control characters or backslashes.", nameof(manifestUrl));
        return Copy(manifestUrl: manifestUrl);
    }

    public IndexedDbSet Include<T>() where T : class, IMagicTableBase, new()
    {
        if (ExcludedTypes.Contains(typeof(T)))
            throw new MagicSchemaException("MAGDB3008", $"Table '{typeof(T).Name}' is both included and excluded in '{SchemaId}'.");
        return Copy(includes: IncludedTypes.Append(typeof(T)));
    }

    public IndexedDbSet Exclude<T>() where T : class, IMagicTableBase, new()
    {
        if (IncludedTypes.Contains(typeof(T)))
            throw new MagicSchemaException("MAGDB3008", $"Table '{typeof(T).Name}' is both included and excluded in '{SchemaId}'.");
        return Copy(excludes: ExcludedTypes.Append(typeof(T)));
    }

    /// <summary>Uses only explicitly included tables.</summary>
    public IndexedDbSet ExplicitTablesOnly() => Copy(discoverDefaultTables: false);

    /// <summary>Includes discovered tables whose default database identifies this declaration.</summary>
    public IndexedDbSet IncludeDefaultTables() => Copy(discoverDefaultTables: true);

    private IndexedDbSet Copy(string? schemaId = null, string? manifestUrl = null,
        bool? discoverDefaultTables = null, IEnumerable<Type>? includes = null,
        IEnumerable<Type>? excludes = null) =>
        new(DatabaseName, schemaId ?? SchemaId, manifestUrl ?? ManifestUrl,
            discoverDefaultTables ?? DiscoverDefaultTables, includes ?? IncludedTypes, excludes ?? ExcludedTypes);
}
