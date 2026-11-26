# Migration schema declarations and C# export

Version 3 uses one C# resolver for runtime database membership and design-time migration export. The result is a language-independent JSON schema. The browser engine consumes that schema and static migration files; it does not load .NET assemblies or execute C# migration classes.

## Declare membership and identity

An `IndexedDbSet` describes a logical database schema. It has a stable schema ID and a default physical database name:

```csharp
using Magic.IndexedDb;
using Magic.IndexedDb.Interfaces;

public sealed class AppDatabases : IMagicRepository
{
    // Discovered models whose GetDefaultDatabase() returns Main belong here.
    public static readonly IndexedDbSet Main = new IndexedDbSet("app-data")
        .WithSchemaId("app-main")
        .WithMigrations("migrations/app-main/manifest.json");

    // An independent schema history with an explicit membership list.
    public static readonly IndexedDbSet Archive = new IndexedDbSet("app-archive")
        .WithSchemaId("app-archive")
        .ExplicitTablesOnly()
        .Include<Customer>()
        .Include<Order>()
        .WithMigrations("migrations/app-archive/manifest.json");
}
```

`Main` and `Archive` are different schema histories in this example. Their physical names can differ between deployments without changing their schema IDs. A schema ID contains 1–128 ASCII letters, digits, dots, underscores, or hyphens and starts with a letter or digit. Use `WithSchemaId` when an existing physical database name contains spaces or other characters.

Membership follows these rules:

1. Automatic discovery selects registered table types whose `GetDefaultDatabase().DatabaseName` matches the declaration's database name.
2. `Include<T>()` adds a table explicitly. Repeating an include has no effect.
3. `Exclude<T>()` removes an automatically selected table. Including and excluding the same type is an error.
4. `ExplicitTablesOnly()` disables automatic selection. `IncludeDefaultTables()` enables it again.
5. Protected models are eligible only for databases configured for authenticated storage. Explicitly including a protected model in an ordinary database is an error.

Each fluent method returns a new declaration. Write the complete expression into a repository field; calling `declaration.Include<T>()` and discarding its return value does not change that declaration. An explicit-only declaration may reuse a model whose default database is not registered in that application. The application must then use its explicitly scoped database query instead of the model's unavailable default route.

Model classes, database declarations, and `GetDefaultDatabase()` must be deterministic at design time. Do not derive schema membership or serialized field names from the signed-in user, a network request, or a mutable process-global variable. Select physical instances at runtime through `Database(declaration, instanceName)`; the declaration itself stays immutable.

## Export from an isolated console project

Create a small console project that references the shared model project and `Magic.IndexedDb`. Target the same supported .NET SDK as those models. This avoids loading application assemblies into the CLI's runtime and avoids starting the Blazor application during export.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <MagicIndexedDbDesignTime>true</MagicIndexedDbDesignTime>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../App.Models/App.Models.csproj" />
    <PackageReference Include="Magic.IndexedDb" Version="3.0.0" />
  </ItemGroup>
</Project>
```

Its `Program.cs` explicitly selects the model and repository types for that application's deployment:

```csharp
using Magic.IndexedDb.Migrations;

return MagicSchemaExporter.Run(
    args,
    tableTypes: [typeof(Customer), typeof(Order)],
    repositoryTypes: [typeof(AppDatabases)]);
```

The CLI invokes this project with `--magic-schema-export`. `Run` returns `0` and writes exactly one JSON document to stdout on success, `1` with an actionable schema diagnostic on stderr for invalid models, or `2` for an invalid invocation. Keep application logging off stdout. The CLI validates the emitted document independently before scaffolding or publishing.

For custom build integration, `MagicSchemaExporter.Export(tableTypes, repositoryTypes, options)` returns the same canonical JSON string. Its envelope is `{ "protocolVersion": 1, "schemas": [...] }`. `MagicSchemaResolver.Resolve(...)` exposes resolved declarations, their table types, their native store metadata, and the portable schema. `ResolveDefinitions(...)` accepts an explicit list of declarations when a repository scan is unnecessary.

Different application projects may use different repository and table lists, including different sets from the same shared model assembly. Keep one design-time exporter per independently deployed model configuration. The CLI can select a schema ID from a multi-schema export.

## Match runtime storage configuration

The exported schema must describe the runtime's actual storage configuration. Pass the same numeric formats and protection policies into the exporter:

```csharp
var options = new MagicSchemaOptions
{
    NumericModes = new Dictionary<string, MagicNumericStorageMode>
    {
        [AppDatabases.Main.DatabaseName] = MagicNumericStorageMode.Lossless
    },
    ProtectedDatabases = [AppDatabases.Main.DatabaseName],
    RecordPolicies = new Dictionary<string,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>>
    {
        [AppDatabases.Main.DatabaseName] = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["customers"] = new[] { "id", "tenantId" }
        }
    }
};

return MagicSchemaExporter.Run(args,
    [typeof(Customer), typeof(Order)], [typeof(AppDatabases)], options);
```

The sample requires `Customer` to use the persisted store name `customers` and persisted public key/index fields `id` and `tenantId`. Configuration maps use the declaration's logical database name, not a runtime instance name. The policies contain field names and codec metadata only. Never put encryption keys, credentials, or account secrets in an exporter or generated schema.

`ProtectedDatabases` includes every declaration opened through authenticated storage, even when its protection is fully expressed by model attributes. It allows the resolver to apply the same model eligibility rule as the runtime. `RecordPolicies` uses persisted store names and persisted exposed field names; `[MagicEncryptRecord]` instead names CLR properties. Conflicting annotations and options fail validation.

A protected schema descriptor does not imply support for arbitrary protected migrations. The browser runtime rejects transitions that lack an authenticated implementation. Keep these limitations explicit in the release and upgrade plan.

## Persisted fields and custom codecs

The exporter follows Magic's property mapping cache and native schema helpers. Its snapshot includes store names, primary keys, ordered compound paths, indexes, persisted property names, nested fields, nullability, enum values/tokens, numeric codecs, and protection metadata. It includes non-indexed fields because a field rename or storage-format change can require migrating records even when IndexedDB's native object-store definition is unchanged.

Dictionaries use the serializer's existing System.Text.Json passthrough path. Their nested field metadata therefore follows `JsonPropertyName`, `JsonIgnore`, standard property visibility, and byte-array base64 encoding where those rules apply. Ordinary mapped Magic objects continue to use Magic's mapping rules. The schema describes the serializer's actual behavior, including that distinction.

The initial exporter rejects System.Text.Json polymorphism, extension-data flattening, and custom number handling inside that passthrough path unless an enclosing versioned converter owns the representation. It also cannot discover additional runtime-derived model types from instances that do not exist at design time. Use declared nested model types or an explicit converter contract for dynamic shapes. These boundaries prevent a seemingly unchanged snapshot from silently overlooking a storage-format change.

An application-owned JSON converter can emit an arbitrary representation that reflection cannot infer. Give its persisted contract a stable, portable ID and explicit version:

```csharp
[JsonConverter(typeof(EpochSecondsConverter))]
[MagicStorageCodec("epoch-seconds", version: 1)]
public DateTime LastSeen { get; set; }
```

`MagicStorageCodecAttribute` lives in `Magic.IndexedDb.Migrations` and can be placed on a converted property or converted type. The exporter emits `epoch-seconds:1`, with an opaque `custom` type, and never serializes the converter's CLR assembly name. Increment the codec version when its persisted representation or interpretation changes, then supply the required migration. Built-in string-enum converters are described automatically. Unversioned arbitrary converters fail with `MAGDB3012` rather than producing an incomplete snapshot.

A whole-record converter similarly owns its entire record representation. Its explicit codec replaces inferred field metadata. Keys and indexes remain explicit native store metadata, and field-only encryption with an arbitrary whole-record converter is rejected. A codec ID is a declaration by the converter author, not a proof that old records can be transformed automatically.

## Deterministic snapshots and validation

Store, index, and field lists are sorted by persisted name. Compound key component order is preserved. Canonical JSON sorts object member names ordinally and uses the same string and numeric encoding as the JavaScript protocol. `MagicSchemaJson.Serialize` produces the canonical text, and `MagicSchemaJson.Fingerprint` returns its `sha256:` fingerprint.

Every resolver invocation validates selected tables, including production configurations. Debug settings do not disable model validation. Duplicate identities, conflicting membership, duplicate store names, invalid keys, unsupported codec metadata, and invalid protection policies fail before a browser mutation. The browser independently verifies its expected model against the deployment manifest before applying changes.

See [schema evolution](schema-evolution.md) for migration authoring and [migration testing](../contributing/migration-testing.md) for the validation matrix.
