# Magic IndexedDB 3.0 upgrade guide

Magic IndexedDB 3.0 is the .NET 10 release. It keeps the established query and database syntax while tightening object materialization, JSON transport, streaming, and browser-resource behavior.

## Runtime compatibility

- The package targets `net10.0`.
- Applications staying on .NET 8 should remain on the 2.x NuGet line.
- The normal consumer syntax for `AddMagicBlazorDB`, `Query<T>()`, database operations, and query composition has not changed.
- `ITypedArgument.Serialize()`, `SerializeToJsonElement()`, and `SerializeToJsonString()` remain public for source and binary-contract stability.

## Constructor materialization

Most models need no changes. Parameterless constructors and existing `[JsonConstructor]` annotations continue to work.

Use `[MagicConstructor]` when a persisted type has multiple constructors and the database materializer should use one specific constructor:

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

Only one `[MagicConstructor]` is allowed per type. Multiple Magic or JSON constructor annotations produce a `MagicConstructorException` with the affected type in the message. Constructor parameter matching is case-insensitive, optional parameter defaults are honored, and writable JSON properties not consumed by the constructor are populated afterward.

## Serialization and streaming corrections

- Backslashes, quotes, newlines, tabs, control characters, and Unicode strings now remain valid JSON and round-trip unchanged.
- Nested collections, arrays, `HashSet<T>`, and dictionaries restore the requested collection shape.
- Configured `System.Text.Json` converters, including enum converters and enums wider than `Int32`, are honored.
- JavaScript arguments use a versioned raw-JSON envelope internally. JavaScript retains the legacy envelope reader, and no calling syntax changes are required.
- `0`, `false`, an empty string, and `null` are returned as their real values instead of being replaced with an empty object.
- `AsAsyncEnumerable()` drains chunks while JavaScript is producing them instead of buffering the full operation first. JavaScript failures propagate to .NET, and interop stream/reference objects are disposed.
- Chunk limits are measured as UTF-8 bytes without splitting Unicode code points.

## Debug behavior

The existing `isDebug` argument now controls JavaScript `debugLog` output at runtime. Diagnostic errors and warnings remain visible. This is a behavior correction; registration syntax is unchanged:

```csharp
builder.Services.AddMagicBlazorDB(BlazorInteropMode.WASM, isDebug: true);
```

## Schema and query corrections

- Assembly scanning tolerates partially loadable assemblies by using the types that did load.
- Explicit enum conversions in comparison expressions are recognized.
- `closeAll()` closes the actual cached Dexie instances.
- Multi-database creation passes each full store definition to `createDb`.
- The Dexie source map is valid BOM-free JSON.

## Migration and ordering status

The automated migration protocol is still under construction. Version 3 does not promise automatic schema migration; test version changes against realistic existing browser data.

Materialized queries apply their requested ordering. Progressive `AsAsyncEnumerable()` delivery does not promise arrival order; if the final order is part of the application contract, materialize and order the yielded values in application code.

## Verification added in 3.0

The repository includes a dedicated .NET 10 unit-test project covering constructor precedence, immutable and hybrid models, public serialization API preservation, escaped strings, dictionaries, nested collections, collection shapes, configured enum converters, explicit enum query casts, and the v2 JavaScript envelope. Browser E2E coverage also exercises escaped/nested records, falsey zero counts, and yield streaming.
