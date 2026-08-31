# Ordering and pagination

IndexedDB does not order data like an in-memory LINQ collection. Whether Magic can use an index, needs a cursor, or streams several query branches affects how and when ordering is applied.

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

You can use one `OrderBy` or `OrderByDescending`. There is no `ThenBy` in the browser query API; use .NET sorting after materialization when you need a second key.

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

`AsAsyncEnumerable()` yields records as they arrive. When a query uses more than one execution path, that arrival order may not match the requested sort order.

When order matters, use `ToListAsync()` or buffer and sort the streamed values:

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

Magic IndexedDB requires `Take` before `Skip`:

```csharp
List<Person> page = await people
    .OrderBy(person => person.Id)
    .Take(pageSize)
    .Skip(offset)
    .ToListAsync();
```

This still means “skip `offset` rows and return `pageSize` rows.” The method order is reversed because IndexedDB applies the limit and offset in that order. The staged interfaces prevent calling `Take` after `Skip`.

Use a positive count for `Take` and `TakeLast`, and a non-negative offset for `Skip`. Avoid zero and negative counts; different query paths do not currently handle them consistently.

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
