# `Where` versus `Cursor`

`Where` and `Cursor` communicate different intent to the query engine.

## Prefer `Where`

```csharp
List<Person> results = await people
    .Where(person => person.Email == email)
    .ToListAsync();
```

`Where` enables query partitioning and index analysis. Depending on the predicate and schema, Magic may use single-field indexes, compound indexes, cursor evaluation, or a combination while de-duplicating records by primary key.

Using `Where` does not guarantee indexed execution. It preserves the opportunity for indexed execution.

## Use `Cursor` for scans

```csharp
List<Person> results = await people
    .Cursor(person => person.Notes.Contains(
        searchText,
        StringComparison.OrdinalIgnoreCase))
    .ToListAsync();
```

`Cursor` sets forced-cursor mode for the query. This is useful when:

- The predicate is inherently scan-oriented, such as string `Contains` or `EndsWith`.
- You require case-insensitive matching that cannot use the relevant IndexedDB index path.
- You need `StableOrdering()`.
- You want to be explicit that index optimization is not expected.

A forced cursor is not an in-memory LINQ query. The browser cursor engine still evaluates the predicate, tracks primary keys, applies additions, and fetches the required records. It is nevertheless expected to scan more data than a selective native index lookup.

## Comparison

| Behavior | `Where` | `Cursor` |
|---|---:|---:|
| Attempts native index use | Yes | No |
| Examines compound indexes | Yes | No |
| Partitions compatible predicate branches | Yes | No |
| May fall back to cursor evaluation | Yes | Always cursor |
| Supports `StableOrdering()` | No | Yes |
| Recommended default | Yes | No |

Both paths support materialized and progressive execution. Both preserve the predicate's logical meaning; they differ in the execution strategy you permit.

## Decision rule

Start with `Where`. Add indexes for common selective lookups. Choose `Cursor` when the query needs a scan or uses a cursor-only feature.

Do not change every query containing one cursor-bound branch to `Cursor` automatically. A `Where` expression containing OR branches may still let Magic execute compatible branches through indexes and reserve cursor evaluation for the remainder, provided the overall query additions allow that plan.
