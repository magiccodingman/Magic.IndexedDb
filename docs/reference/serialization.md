# Serialization

Magic IndexedDB uses `System.Text.Json` to send records between .NET and the browser. IndexedDB stores the JavaScript values produced from that JSON, and Magic converts them back to your C# types when you read them.

Most ordinary models work without extra configuration:

| C# value | How it is stored |
|---|---|
| Strings, booleans, and nulls | As their normal JSON values |
| Numeric types and `decimal` | As JavaScript numbers; see [numeric precision](#numeric-precision) |
| `DateTime`, `DateTimeOffset`, `Guid`, `Uri`, and `TimeSpan` | Using their `System.Text.Json` representation |
| Nullable values | As either `null` or the underlying value |
| Enums | As numbers by default, or names when a string-enum converter is used |
| Nested objects | Recursively |
| Arrays, lists, and sets | As arrays, restored to the declared collection type |
| Dictionaries | As JSON objects |

Being able to store a value does not mean every part of it can be queried. For example, you can store a nested object or dictionary, but Magic cannot translate an arbitrary predicate against its contents. The supported predicate shapes are listed in the [query expression reference](query-expressions.md).

## Numeric precision

Numbers pass through JavaScript before reaching IndexedDB. JavaScript cannot represent every `long`, `ulong`, or high-precision `decimal` value exactly.

Be careful with large numeric identifiers, money, and concurrency values. Depending on the data, a safer representation may be:

- a `Guid` for an identifier;
- a large integer or decimal stored as an invariant string; or
- money stored in minor units, provided the largest possible value is still safe in JavaScript.

If exact precision matters, test the largest values through the browser. A .NET-only serialization test will not catch precision lost in JavaScript.

## Dates and times

`DateTime` and `DateTimeOffset` can be stored normally. Your application still needs a consistent rule for what each value means: UTC, local time, or an offset-bearing instant.

Magic supports the date comparisons described in the [query expression reference](query-expressions.md), but it does not translate every property or method available on a .NET date type.

Changing how an existing property represents time can make older records incompatible even if the property name stays the same.

## Enums

Enums are stored as numbers by default. Add `JsonStringEnumConverter` to an enum when you prefer to store its names:

```csharp
using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))]
public enum OrderStatus
{
    Pending,
    Complete
}
```

The converter is also used when Magic builds equality filters. Do not switch an existing property between numeric and named enum storage without handling the records already in the database.

## Property names and ignored properties

- `[MagicName]`, `[MagicIndex]`, and `[MagicUniqueIndex]` can set the name stored in IndexedDB.
- `[MagicNotMapped]` leaves a property out of stored records and ignores it when records are read.
- Extra properties in an older stored record are ignored when the current model no longer contains them.
- Properties missing from a stored record keep their constructor or default value, unless they are required by the selected constructor.
- A default-valued auto-increment key is left out of an insert so IndexedDB can generate it.

Once data has been deployed, changing a stored property name is much like renaming a database column: existing records still use the old name. See [schema evolution](../guides/schema-evolution.md) before making that change.

## Constructors

When Magic creates an object from a stored record, it chooses a constructor in this order:

1. The constructor marked `[MagicConstructor]`
2. The constructor marked `[JsonConstructor]`
3. A public parameterless constructor
4. The only constructor on the type
5. For older ambiguous models, the constructor with the most parameters

Constructor parameters are matched to properties without regard to case, and optional parameter defaults are honored. Magic fills any remaining writable properties after the object is constructed.

`Query<T>()` still requires the table type to satisfy its `new()` constraint. Constructor-based materialization is therefore most useful for nested types, or for a table model that also keeps a public parameterless constructor.

See [schema attributes and constructors](schema-attributes.md) for examples.

## Collections and dictionaries

Magic can restore arrays, lists, sets, and collection types that can be constructed from `IEnumerable<T>`. It will not silently substitute a different type when it cannot recreate a custom collection.

Dictionary keys follow the normal `System.Text.Json` rules. In a `Dictionary<string, object>`, values will often come back as `JsonElement`; your code should read those elements as the expected type.

## Custom converters

Converter attributes on a persisted type or property are used during normal database operations. There is currently no global `JsonSerializerOptions` setting on `IMagicIndexedDb`.

`MagicJsonSerializationSettings` and `MagicSerializationHelper` are useful for direct serialization calls and tests. Settings passed to those helpers do not change the serializer used by the injected database service.

For an unusual type or converter, test a real insert and read after closing and reopening the database. Include nulls, empty values, Unicode text, boundary numbers, and data written by the previous version of your application. Test its queries separately, because a successful round trip does not guarantee that the type can be used in a predicate.
