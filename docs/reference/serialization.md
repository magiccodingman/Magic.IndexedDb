# Serialization and persisted types

Magic IndexedDB serializes C# values with `System.Text.Json`, transports JSON through JavaScript, stores the resulting values in IndexedDB, and applies Magic's materializer when records return to .NET. A type working in an in-process JSON round trip does not by itself prove that every value of that type is lossless through JavaScript and the browser database.

## Supported model shapes

The current serializer supports these principal shapes:

| Shape | Current behavior |
|---|---|
| Strings, booleans, and nulls | Preserve their JSON meanings, including escaped and Unicode text. |
| Primitive numeric types and `decimal` | Serialize as JSON numbers; JavaScript precision constraints apply. |
| `DateTime`, `DateTimeOffset`, `Guid`, `Uri`, and `TimeSpan` | Use their `System.Text.Json` representations. Query translation support is narrower than storage support. |
| Nullable values | Preserve null separately from the underlying value. |
| Enums | Numeric by default; a configured string-enum converter can store names. |
| Nested objects | Persist recursively, including Magic persisted-name mappings. |
| Arrays and `List<T>` | Restore as the requested array or list shape. |
| `HashSet<T>` | Restores as a set. |
| Dictionaries | Persist as JSON objects, including dictionaries nested in entities and collections. |
| Nested collections | Restore supported concrete collection shapes recursively. |

Storage support and query support are separate contracts. A nested object, dictionary, or collection can be stored without making arbitrary members inside it indexable or translatable. See the [query expression reference](query-expressions.md).

## Numeric precision across JavaScript

JSON numbers become JavaScript `Number` values in the browser transport. Integers outside JavaScript's exactly representable range, and high-precision decimal values, can lose precision even though `long`, `ulong`, and `decimal` serialize successfully in .NET.

Do not use an unverified large numeric value as a primary key, unique identity, money representation, or concurrency token. Safer persisted representations include:

- A `Guid` for identity
- A decimal or large integer encoded as an invariant string
- Money stored as an integer minor-unit value only when the entire expected range remains exactly representable

Test the largest and smallest production values through a real browser, not only through `MagicSerializationHelper`.

## Dates and times

`DateTime` and `DateTimeOffset` can be persisted, but their semantic meaning still belongs to the application. Decide whether a value represents UTC, a local wall-clock time, or an offset-bearing instant before persisting it.

The query translator specifically documents direct `DateTime` comparisons and supported date-member expressions. Persisting a time-related CLR type does not automatically make every property or method on that type translatable.

Changing an existing property's date representation, offset policy, or JSON converter is a schema/data migration even if the C# property name does not change.

## Enums

Numeric enum storage is the default. A `JsonStringEnumConverter` placed on the enum type changes its persisted representation to a name:

```csharp
using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))]
public enum OrderStatus
{
    Pending,
    Complete
}
```

Type-level converter metadata is honored by ordinary persisted records and equality filters. Switching an existing database between numeric and named enum storage does not rewrite old records. See [schema evolution](../guides/schema-evolution.md).

## Persisted names and ignored members

- `[MagicName]`, `[MagicIndex]`, and `[MagicUniqueIndex]` can define a stable persisted property name.
- `[MagicNotMapped]` prevents a property from being written and ignores it during materialization.
- Unknown stored properties are ignored when the current model does not map them.
- Missing stored properties retain constructor/default values unless they are constructor-bound as described below.
- A default-valued auto-increment primary key is omitted from the serialized insert so IndexedDB can generate it.

Persisted names are data contracts. Renaming or removing them requires the same planning as changing a column in another database.

## Constructor materialization

Constructor selection follows this order:

1. The single constructor marked `[MagicConstructor]`
2. The single constructor marked `[JsonConstructor]`
3. A public parameterless constructor
4. The only available constructor
5. For legacy ambiguous types, the constructor with the most parameters, with a deterministic tie-break

Constructor parameter matching is case-insensitive. Optional parameter defaults are honored. After construction, remaining writable properties are populated.

The table type itself must still satisfy the `new()` constraint required by `Query<T>()`; constructor-based materialization is most useful for nested persisted types and for table models that retain a public parameterless constructor alongside a selected materialization constructor.

See [schema attributes and constructors](schema-attributes.md) for examples and precedence details.

## Collection restoration

Arrays, lists, sets, and collection types constructible from `IEnumerable<T>` can be restored. An arbitrary custom `IEnumerable<T>` implementation is not automatically supported. If Magic cannot reconstruct the requested concrete collection from a JSON array, deserialization fails instead of silently substituting a different declared type.

Dictionary properties follow `System.Text.Json` dictionary-key rules. With a value type such as `object`, individual values commonly materialize as `JsonElement`; application code should interpret those elements explicitly.

## Custom converters and advanced helpers

Converter attributes declared on persisted types and properties participate in normal serialization. The ordinary `IMagicIndexedDb` registration API does not expose a global `JsonSerializerOptions` hook.

`MagicJsonSerializationSettings` and `MagicSerializationHelper` are public compatibility and testing helpers. Passing settings to those helpers customizes that helper call; it does not reconfigure the injected database service.

## Verification checklist

For every application-specific type or converter:

1. Insert representative minimum, maximum, null, empty, and non-ASCII values.
2. Close and reopen the database.
3. Read the record in each supported browser engine.
4. Compare exact values, not only successful deserialization.
5. Exercise any equality or range queries separately from the storage round trip.
6. Repeat the check with data created by the previous application version before deploying a representation change.
