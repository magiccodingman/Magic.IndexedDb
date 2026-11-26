using System.Text.Json.Serialization;

namespace Magic.IndexedDb.Migrations;

/// <summary>The language-independent persisted model. Revision belongs to the manifest, not this model.</summary>
public sealed record MagicSchemaDocument
{
    public int ProtocolVersion { get; init; } = 1;
    public required string SchemaId { get; init; }
    public string NumericFormat { get; init; } = "legacy";
    public IReadOnlyList<MagicStoreDefinition> Stores { get; init; } = [];
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = [];
}

public sealed record MagicStoreDefinition
{
    public required string Name { get; init; }
    /// <summary>A string, ordered string array, or null for out-of-line keys.</summary>
    public object? KeyPath { get; init; }
    public bool AutoIncrement { get; init; }
    public IReadOnlyList<MagicIndexDefinition> Indexes { get; init; } = [];
    public IReadOnlyList<MagicFieldDefinition> Fields { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? EncryptedFields { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ExposedFields { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecordCodec { get; init; }
}

public sealed record MagicIndexDefinition
{
    public required string Name { get; init; }
    public required object KeyPath { get; init; }
    public bool Unique { get; init; }
    public bool MultiEntry { get; init; }
}

public sealed record MagicFieldDefinition
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool Nullable { get; init; }
    public string Codec { get; init; } = "json";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MagicFieldDefinition>? Properties { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MagicFieldDefinition? Items { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? EnumValues { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? EnumNames { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EnumUnderlyingType { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EnumFlags { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MagicFieldDefinition? DictionaryKeys { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WriteCondition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reference { get; init; }
}

/// <summary>Schema-affecting configuration, shared by the runtime and a design-time exporter.</summary>
public sealed class MagicSchemaOptions
{
    /// <summary>Logical database names opened through the authenticated storage configuration.</summary>
    public IReadOnlyCollection<string> ProtectedDatabases { get; init; } = [];

    public IReadOnlyDictionary<string, MagicNumericStorageMode> NumericModes { get; init; } =
        new Dictionary<string, MagicNumericStorageMode>(StringComparer.Ordinal);

    /// <summary>Database name → persisted store name → exposed persisted fields.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyCollection<string>>> RecordPolicies { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyCollection<string>>>(StringComparer.Ordinal);
}

/// <summary>Resolved C# linkage; only <see cref="Schema"/> is part of the portable protocol.</summary>
public sealed class ResolvedMagicDatabase
{
    public IndexedDbSet Definition { get; }
    public MagicSchemaDocument Schema { get; }
    public IReadOnlyList<Type> TableTypes { get; }
    public IReadOnlyList<StoreSchema> StoreSchemas { get; }

    internal ResolvedMagicDatabase(IndexedDbSet definition, MagicSchemaDocument schema,
        Type[] tableTypes, StoreSchema[] storeSchemas)
    {
        Definition = definition;
        Schema = schema;
        TableTypes = Array.AsReadOnly(tableTypes);
        StoreSchemas = Array.AsReadOnly(storeSchemas);
    }
}

/// <summary>An actionable schema diagnostic with a stable machine-readable code.</summary>
public sealed class MagicSchemaException : InvalidOperationException
{
    public string Code { get; }
    public string DocumentationUrl =>
        "https://github.com/magiccodingman/Magic.IndexedDb/blob/master/docs/upgrading/v3.md#diagnostics";

    public MagicSchemaException(string code, string message, Exception? innerException = null)
        : base($"{code}: {message}", innerException) => Code = code;
}

/// <summary>Declares a stable portable contract for an application-owned JSON converter.</summary>
/// <remarks>Increment the version whenever the converter changes the persisted representation.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property, Inherited = true)]
public sealed class MagicStorageCodecAttribute : Attribute
{
    public string Id { get; }
    public int Version { get; }
    public MagicStorageCodecAttribute(string id, int version = 1)
    {
        MagicSchemaResolver.ValidateSchemaId(id);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        Id = id;
        Version = version;
    }
}
