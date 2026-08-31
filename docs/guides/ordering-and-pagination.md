# Ordering and pagination

Ordering is part of the query plan, but IndexedDB is not an in-memory LINQ provider. The available index paths, cursor fallback, de-duplication, and progressive delivery all affect how ordering can be executed.

## Ordering a materialized query

```csharp
List<Person> ordered = await people
    .OrderBy(person => person.LastName)
    .Take(20)
    .ToListAsync();
```

`ToListAsync()` applies the requested query ordering before materialization. An indexed ordering has the best chance of staying on an IndexedDB-native path. Ordering by a non-indexed property requires cursor processing.

`OrderByDescending` follows the same rules:

```csharp
List<Person> newest = await people
    .OrderByDescending(person => person.CreatedAt)
    .Take(20)
    .ToListAsync();
```

The current fluent API exposes one explicit `OrderBy` or `OrderByDescending`; it is not the full `IOrderedQueryable<T>` surface and does not expose `ThenBy`.

## Filtering and ordering

`Where(...)` returns `IMagicQueryStaging<T>`, which does not expose `OrderBy`. Choose one of these explicit patterns when a filtered result also needs ordering:

```csharp
// Browser cursor filtering and ordering.
List<Person> orderedMatches = await people
    .Cursor(person => person.IsActive)
    .OrderBy(person => person.LastName)
    .ToListAsync();
```

```csharp
// Preserve Where planning, then order the materialized result in .NET.
List<Person> matches = await people
    .Where(person => person.IsActive)
    .ToListAsync();

List<Person> orderedMatches = matches
    .OrderBy(person => person.LastName)
    .ThenBy(person => person.Id)
    .ToList();
```

Do not generate `people.Where(...).OrderBy(...)`; that chain is not present on the current staged interface.

## Progressive results

`AsAsyncEnumerable()` prioritizes yielding records progressively. It does not promise that arrival order is the final requested order when work is split across execution paths.

If final order is part of the application contract, either use `ToListAsync()` or explicitly materialize and sort the streamed values:

```csharp
List<Person> buffered = [];

await foreach (Person person in people
    .Where(person => person.IsActive)
    .AsAsyncEnumerable(cancellationToken))
{
    buffered.Add(person);
}

List<Person> ordered = buffered
    .OrderBy(person => person.LastName)
    .ThenBy(person => person.Id)
    .ToList();
```

## `Take` and `Skip`

Magic IndexedDB's fluent contract requires `Take` before `Skip`:

```csharp
List<Person> page = await people
    .OrderBy(person => person.Id)
    .Take(pageSize)
    .Skip(offset)
    .ToListAsync();
```

In application terms, this represents the familiar pagination intent: skip `offset` rows and return `pageSize` rows. The method order is intentionally reversed because of the way the IndexedDB/Dexie path composes its limit and offset operations. The staged interfaces prevent calling `Take` after `Skip`.

For page-number pagination:

```csharp
int offset = (pageNumber - 1) * pageSize;

List<Person> page = await people
    .OrderBy(person => person.Id)
    .Take(pageSize)
    .Skip(offset)
    .ToListAsync();
```

Always supply a deterministic ordering for repeatable pages. Without a useful explicit order, changes to data or the chosen execution path can change page membership.

## `TakeLast`

```csharp
List<Person> lastFive = await people
    .OrderBy(person => person.CreatedAt)
    .TakeLast(5)
    .ToListAsync();
```

Magic may transform `TakeLast` using reverse traversal and a limit when the order/index path allows it. Otherwise, the cursor engine applies the requested semantics.

## Compound primary keys and ordering

One component of a compound primary key is not a standalone IndexedDB index. `OrderBy` or `OrderByDescending` on such a component uses a semantics-safe cursor path unless that property also has a real ordinary or unique index. A simple single-field primary key and a declared standalone index remain eligible for indexed ordering.

Add `[MagicIndex]` to a compound-key component only when independent indexed filtering or ordering is an intentional part of the schema. Adding an index to an already deployed database is a schema change and requires the planning described in [schema evolution](schema-evolution.md).

## Stable cursor ordering

The cursor engine normally considers explicit ordering, useful indexed predicate fields, and row insertion order when it builds a deterministic fallback order. Optimizer rewrites can change which indexed fields participate.

Use `StableOrdering()` when a forced-cursor query must ignore inferred indexed predicate-field ordering and retain the cursor's stable scan order:

```csharp
List<Person> page = await people
    .Cursor(person => person.Age > 30 || person.Name == "Ada")
    .StableOrdering()
    .Take(20)
    .ToListAsync();
```

`StableOrdering()` forces cursor execution. In the current staged API, it returns `IMagicCursorStage<T>` and cannot be combined with a subsequent `OrderBy`; it stabilizes fallback cursor order rather than acting as a general explicit-sort modifier. It is not needed merely to make `ToListAsync()` respect an ordinary `OrderBy`.
