# Moving version 2 projects to .NET 10

The .NET 10 package keeps the version 2 query and database API. Most applications only need to update their target framework and package version, but a few serialization and constructor details are worth checking.

## Before upgrading

- The current package targets `net10.0`.
- Applications staying on .NET 8 should use an earlier compatible package release.
- Registration, `Query<T>()`, database operations, and query composition keep the same shape.
- `ITypedArgument.Serialize()`, `SerializeToJsonElement()`, and `SerializeToJsonString()` remain available.

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

One convention has changed: when an unannotated type has both a public parameterless constructor and one or more parameterized constructors, Magic now chooses the parameterless constructor. Add `[MagicConstructor]` when it should choose a parameterized constructor instead.

Read [schema attributes and constructors](../reference/schema-attributes.md) for shared and separate JSON/Magic examples.

## Serialization and streaming corrections

- Backslashes, quotes, newlines, tabs, control characters, and Unicode strings remain valid JSON and round-trip unchanged.
- Nested collections, arrays, `HashSet<T>`, and dictionaries restore supported requested collection shapes, including dictionaries nested inside entities and collections.
- Enum-type `System.Text.Json` string converters are honored consistently by stored records and equality filters; numeric enum storage remains the default.
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
- Unrelated member conversions remain unsupported rather than being translated with changed semantics.
- Closing all cached connections internally closes the actual Dexie instances.
- Multi-database creation passes each complete store definition to database creation.
- The bundled Dexie source map is valid BOM-free JSON.
- String `==` and supported `Equals` expressions retain case-sensitive C# equality semantics.
- Negated equality, string equality, and stored collection containment use the `NotEqual`, `Equal`, and `Contains` operations.
- Empty captured membership matches no rows; supported empty `Any` and `All` expressions retain false and true respectively.
- Mixed constant boolean expressions preserve normal AND/OR identities.
- `.Date` inequalities use full-day boundaries, including nullable not-equal comparisons.
- Compound-index selection never discards residual predicates; a semantics-safe cursor fallback evaluates the complete branch when necessary.
- Ordering by one component of a compound primary key falls back to a cursor unless the component also has a standalone index.

## Schema changes and streamed ordering

Magic does not currently migrate existing records when a schema changes. Test changes against data created by the previous application version. See [schema evolution](../guides/schema-evolution.md).

Materialized queries apply their requested ordering. `AsAsyncEnumerable()` can yield records from separate query branches out of order, so use materialization or sort the streamed values when order matters.
