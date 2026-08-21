# Moving version 2 projects to .NET 10

Magic IndexedDB remains on its version 2 release line. Moving the project to .NET 10 is a target-framework update, not a new engine generation. The established query and database syntax remains in place while object materialization, JSON transport, streaming, and browser-resource behavior receive compatibility and reliability corrections.

## Runtime compatibility

- The current package targets `net10.0`.
- Applications staying on .NET 8 should use an earlier compatible package release.
- Normal consumer syntax for `AddMagicBlazorDB`, `Query<T>()`, database operations, and query composition remains recognizable.
- `ITypedArgument.Serialize()`, `SerializeToJsonElement()`, and `SerializeToJsonString()` remain public for compatibility.

## Constructor materialization

Most models need no changes. Public parameterless constructors and existing `[JsonConstructor]` annotations continue to work.

Use `[MagicConstructor]` when a persisted type has multiple constructors and the database materializer should use a specific one:

```csharp
using Magic.IndexedDb.SchemaAnnotations;

public sealed class Customer
{
    public int Id { get; }
    public string Name { get; }
    public string? Notes { get; set; }

    [MagicConstructor]
    public Customer(int id, string name)
    {
        Id = id;
        Name = name;
    }
}
```

Constructor selection follows this order:

1. The single constructor marked `[MagicConstructor]`.
2. The single constructor marked `[JsonConstructor]`.
3. A public parameterless constructor.
4. The only public constructor, when exactly one exists.
5. The legacy public constructor with the most parameters, with deterministic tie-breaking.

Only one `[MagicConstructor]` is allowed per type. Multiple Magic or JSON constructor annotations produce a `MagicConstructorException`. Constructor parameter matching is case-insensitive, optional defaults are honored, and writable JSON properties not consumed by the constructor are populated afterward.

The observable convention change is for an unannotated type that has both a public parameterless constructor and one or more parameterized constructors: the current materializer chooses the parameterless constructor. Add `[MagicConstructor]` when the parameterized constructor is the intended persistence contract.

Read [schema attributes and constructors](../reference/schema-attributes.md) for shared and separate JSON/Magic examples.

## Serialization and streaming corrections

- Backslashes, quotes, newlines, tabs, control characters, and Unicode strings remain valid JSON and round-trip unchanged.
- Nested collections, arrays, `HashSet<T>`, and dictionaries restore supported requested collection shapes.
- Configured `System.Text.Json` converters, including enum converters and enums wider than `Int32`, are honored.
- JavaScript arguments use a versioned raw-JSON envelope internally. JavaScript retains the earlier envelope reader; consumer call syntax does not change.
- `0`, `false`, an empty string, and `null` are returned as their real values rather than being replaced with an empty object.
- `AsAsyncEnumerable()` drains chunks while JavaScript is producing them. JavaScript failures propagate to .NET, and interop stream/reference objects are disposed.
- Chunk limits are measured as UTF-8 bytes without splitting Unicode code points.

## Debug behavior

The existing `isDebug` argument now controls JavaScript informational output at runtime. Errors and warnings remain visible.

```csharp
builder.Services.AddMagicBlazorDB(
    BlazorInteropMode.WASM,
    isDebug: true);
```

## Schema and query corrections

- Assembly scanning tolerates partially loadable assemblies by using the types that did load.
- Explicit enum conversions in comparison expressions are recognized.
- Closing all cached connections internally closes the actual Dexie instances.
- Multi-database creation passes each complete store definition to database creation.
- The bundled Dexie source map is valid BOM-free JSON.

## Migration and ordering status

The automated migration protocol is still under construction. Magic IndexedDB does not promise automatic schema migration; test schema changes against realistic existing browser data. See [schema evolution](../guides/schema-evolution.md).

Materialized queries apply their requested ordering. Progressive `AsAsyncEnumerable()` delivery does not promise arrival order; if final order is part of the application contract, use materialization or explicitly sort the yielded values afterward.

## Verification

The repository includes a .NET 10 unit-test project covering constructor precedence, immutable and hybrid models, public serialization API preservation, escaped strings, dictionaries, nested collections, collection shapes, configured enum converters, explicit enum query casts, and the earlier JavaScript envelope.

Browser end-to-end coverage also exercises escaped and nested records, falsey zero counts, and yield streaming.
