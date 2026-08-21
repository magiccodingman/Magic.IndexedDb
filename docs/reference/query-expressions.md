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
| `values.Contains(x.Value)` | `In` | Index-capable |

Compatible lower and upper bounds on one field may be compressed into a range. Multiple equality alternatives on one field may be compressed into an `anyOf` lookup.

## Strings and collections

| C# expression family | Universal operation | Typical path |
|---|---|---|
| `x.Text.StartsWith(value)` | `StartsWith` | Index-capable in supported modes; case rules can require cursor fallback |
| `!x.Text.StartsWith(value)` | `NotStartsWith` | Cursor |
| `x.Text.EndsWith(value)` | `EndsWith` | Cursor |
| `!x.Text.EndsWith(value)` | `NotEndsWith` | Cursor |
| `x.Text.Contains(value)` | `Contains` | Cursor |
| `!x.Text.Contains(value)` | `NotContains` | Cursor |
| `x.Text.Length == n` | `LengthEqual` | Cursor |
| Other length comparisons | `Length*` | Cursor |

Supported `StringComparison` overloads carry case-sensitivity intent into the universal condition. Case-insensitive matching normally requires cursor processing.

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

## Booleans and enums

```csharp
await people.Where(person => person.IsActive).ToListAsync();

await people.Where(person =>
    person.Access == Permissions.CanRead).ToListAsync();

await people.Where(person =>
    (long)person.Access >= (long)Permissions.CanRead).ToListAsync();
```

Version 3 recognizes explicit enum conversions used in comparisons. JSON enum converters configured through Magic's serialization settings are also honored during serialization.

## Logical composition

```csharp
await people.Where(person =>
    (person.Age >= 18 && person.Age < 30) ||
    (person.IsActive && person.City == "Boston"))
    .ToListAsync();
```

The translator preserves nested AND/OR intent. The optimizer can split OR alternatives into query branches. Every condition in an AND branch must be representable by a compatible index or compound-index path for that entire branch to avoid cursor fallback.

Query additions such as ordering, first/last, take, and skip can require branches to be evaluated together so that the operation applies to the combined result rather than independently to every branch.

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

This reference describes version 3, not every method LINQ exposes. A method being legal inside a C# expression tree does not make it translatable. Verify new expression shapes with tests against realistic IndexedDB data before depending on them in production.
