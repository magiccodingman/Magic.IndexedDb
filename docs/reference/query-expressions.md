# Query expression reference

Magic IndexedDB translates supported C# expression-tree shapes into its universal predicate language. The tables below describe the principal operations and their likely IndexedDB execution path.

“Index-capable” means the operation can use an appropriate declared index in a compatible predicate plan. It is not a guarantee that a particular compound expression will remain indexed.

## Comparisons

| C# expression | Universal operation | Typical path |
|---|---|---|
| `x.Value == value` | `Equal` | Index-capable |
| `x.Value != value` | `NotEqual` | Cursor |
| `x.Value > value` | `GreaterThan` | Index-capable |
| `x.Value >= value` | `GreaterThanOrEqual` | Index-capable |
| `x.Value < value` | `LessThan` | Index-capable |
| `x.Value <= value` | `LessThanOrEqual` | Index-capable |
| `values.Contains(x.Value)` | Equality alternatives, possibly optimized to `In` | Index-capable |

Compatible lower and upper bounds on one field may be compressed into a range. Multiple equality alternatives on one field may be compressed into an `anyOf` lookup.

Reversed comparisons such as `minimum < x.Value` are normalized without changing operand meaning. Comparisons between two record properties and arithmetic such as `x.Value + 1 > limit` are not supported translation shapes.

## Strings and collections

| C# expression family | Universal operation | Typical path |
|---|---|---|
| `x.Text == value` or `x.Text.Equals(value)` | `Equal` | Index-capable and case-sensitive |
| `x.Text.StartsWith(value)` | `StartsWith` | Index-capable in supported modes; case rules can require cursor fallback |
| `!x.Text.StartsWith(value)` | `NotStartsWith` | Cursor |
| `x.Text.EndsWith(value)` | `EndsWith` | Cursor |
| `!x.Text.EndsWith(value)` | `NotEndsWith` | Cursor |
| `x.Text.Contains(value)` | `Contains` | Cursor |
| `!x.Text.Contains(value)` | `NotContains` | Cursor |
| `x.Text.Length == n` | `LengthEqual` | Cursor |
| Other length comparisons | `Length*` | Cursor |
| `x.Values.Contains(3)` | `Contains` | Cursor |

Supported `StringComparison` overloads carry case-sensitivity intent into the universal condition. The transport records case-sensitive versus case-insensitive intent; it does not preserve a separate browser execution mode for every .NET culture value. Case-insensitive matching normally requires cursor processing.

Do not confuse stored collection containment with captured membership:

```csharp
int[] allowedAges = [18, 21, 30];

// Captured membership: property equals one of the supplied values.
await people.Where(person => allowedAges.Contains(person.Age)).ToListAsync();

// Stored collection containment: each record's collection is inspected.
await people.Where(person => person.TagIds.Contains(42)).ToListAsync();
```

An empty captured membership sequence matches no rows. The documented stored-collection shape takes a literal constant; a captured variable as the `Contains` argument is not currently translated by this shape.

## Nulls

| C# expression | Universal operation | Typical path |
|---|---|---|
| `x.Value == null` | `IsNull` | Cursor |
| `x.Value != null` | `IsNotNull` | Cursor |

Null checks treat an absent JavaScript property consistently with the engine's null/undefined handling. Remember that a predicate requiring a missing older field may exclude that row.

## Dates and times

Direct `DateTime` equality and range comparisons can use an index when the stored property is indexed and the plan is otherwise compatible. `DateOnly` is also recognized by the translator in supported expression shapes.

| C# member expression | Universal operation family | Typical path |
|---|---|---|
| `x.When.Date == date` | Day range | Index-capable |
| `x.When.Date != date` | Before-day OR after-day range | Index-capable branches; plan-dependent |
| `x.When.Date > date` | At or after the next day | Index-capable |
| `x.When.Date <= date` | Before the next day | Index-capable |
| `x.When.Year == year` | `YearEqual` | Index-capable in supported plans |
| Other `.Year` comparisons | `Year*` | Equality/range may be index-capable |
| `.Month` comparisons | `Month*` | Cursor |
| `.Day` comparisons | `Day*` | Cursor |
| `.DayOfWeek` comparisons | `DayOfWeek*` | Cursor |
| `.DayOfYear` comparisons | `DayOfYear*` | Cursor |

Nullable date members may require `.Value` to make the member accessible in C#:

```csharp
List<Person> bornIn2020 = await people
    .Where(person => person.DateOfBirth.Value.Year == 2020)
    .ToListAsync();
```

Only use `.Value` as part of a supported translated member expression. It is not a general replacement for a null guard.

`.Date` comparisons are normalized to full-day boundaries. Equality means `>=` the start of the selected day and `<` the start of the next day. `>` starts at the next day, and `<=` ends immediately before the next day. For a nullable date, the supported `.Value.Date != target` translation includes null values as not equal; the other component comparisons exclude null values.

## Booleans and enums

```csharp
await people.Where(person => person.IsActive).ToListAsync();

await people.Where(person =>
    person.Access == Permissions.CanRead).ToListAsync();

await people.Where(person =>
    (long)person.Access >= (long)Permissions.CanRead).ToListAsync();
```

Enums are stored and queried as their numeric values by default. The translator recognizes the compiler conversions used by ordinary enum equality and explicit integral casts without treating unrelated property casts as equivalent queries.

To persist names instead, place a `System.Text.Json` string-enum converter on the enum type:

```csharp
using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<Permissions>))]
public enum Permissions
{
    None,
    CanRead,
    CanWrite
}
```

Magic then uses the same string representation for stored records and translated equality filters. String-backed enum queries support equality and inequality; numeric range casts are rejected because their ordering does not describe the persisted strings.

Changing an existing property from numeric enum storage to named storage changes its persisted representation. Existing numeric records are not automatically rewritten, so plan a migration or rebuild disposable data before switching.

## Logical composition

```csharp
await people.Where(person =>
    (person.Age >= 18 && person.Age < 30) ||
    (person.IsActive && person.City == "Boston"))
    .ToListAsync();
```

The translator preserves nested AND/OR intent. The optimizer can split OR alternatives into query branches. Every condition in an AND branch must be representable by a compatible index or compound-index path for that entire branch to avoid cursor fallback.

Query additions such as ordering, first/last, take, and skip can require branches to be evaluated together so that the operation applies to the combined result rather than independently to every branch.

Literal boolean nodes and supported captured-sequence quantifiers preserve normal truth rules:

| Expression shape | Meaning |
|---|---|
| `x => true` | Match every row |
| `x => false` | Match no rows |
| `true && predicate` or `false || predicate` | Preserve `predicate` |
| `true || predicate` | Match every row |
| `false && predicate` | Match no rows |
| `values.Any(value => predicate)` with empty captured `values` | False |
| `values.All(value => predicate)` with empty captured `values` | True |

Captured `Any` and `All` are expanded into boolean branches. Use them for bounded application values, not as a substitute for arbitrary correlated subqueries.

Negation is supported for documented binary comparisons, logical groups, and the documented string methods. Unary `!x.BooleanProperty` is not part of the supported contract; write `x.BooleanProperty == false`.

## Query additions

| C# method | Universal addition |
|---|---|
| `OrderBy` | `orderBy` |
| `OrderByDescending` | `orderByDescending` |
| `FirstOrDefaultAsync` | `first` |
| `LastOrDefaultAsync` | `last` |
| `Take` | `take` |
| `Skip` | `skip` |
| `TakeLast` | `takeLast` |
| `StableOrdering` | `stableOrdering` |

See [ordering and pagination](../guides/ordering-and-pagination.md) for the valid fluent order and output-order contract.

## Treat the list as versioned

This reference describes the current supported expression surface, not every method LINQ exposes. A method being legal inside a C# expression tree does not make it translatable. Verify new expression shapes with tests against realistic IndexedDB data before depending on them in production.

Nested objects and collections can be persisted without implying that arbitrary nested-member predicates are translatable. The documented query surface targets direct table properties plus the explicit date, nullable, length, enum, and collection shapes described above.
