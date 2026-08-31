# Public API reference

This page summarizes the supported application surface. It is a guide to the interfaces and return contracts, not generated API documentation.

For cross-cutting guarantees such as bulk atomicity, cancellation, serialization, and browser persistence, start with the [behavioral contract index](behavioral-contracts.md).

## Registration

```csharp
IServiceCollection AddMagicBlazorDB(
    BlazorInteropMode interoptMode,
    bool isDebug);

IServiceCollection AddMagicBlazorDB(
    long jsMessageSizeBytes,
    bool isDebug);
```

`BlazorInteropMode.WASM` uses a 15 MiB message limit. `BlazorInteropMode.SignalR` uses 31 KiB.

## `IMagicIndexedDb`

```csharp
ValueTask<IMagicQuery<T>> Query<T>();

ValueTask<IMagicQuery<T>> Query<T>(
    Func<T, IndexedDbSet> dbSetSelector);

Task<QuotaUsage> GetStorageEstimateAsync(
    CancellationToken cancellationToken = default);

ValueTask<IMagicDatabaseScoped> Database(
    IndexedDbSet indexedDbSet);
```

`T` must be a class implementing `IMagicTableBase` with a public parameterless constructor. `Query<T>()` selects the table's default database; the selector overload chooses one of its strongly typed database associations.

## `IMagicQuery<T>`

Table identity:

```csharp
string DatabaseName { get; }
string SchemaName { get; }
```

Query composition:

```csharp
IMagicQueryStaging<T> Where(Expression<Func<T, bool>> predicate);
IMagicCursor<T> Cursor(Expression<Func<T, bool>> predicate);

IMagicQueryOrderableTable<T> OrderBy(
    Expression<Func<T, object>> selector);

IMagicQueryOrderableTable<T> OrderByDescending(
    Expression<Func<T, object>> selector);

IMagicQueryPaginationTake<T> Take(int amount);
IMagicQueryFinal<T> TakeLast(int amount);
IMagicQueryFinal<T> Skip(int amount);
```

Shared execution through `IMagicExecute<T>`:

```csharp
Task<List<T>> ToListAsync();
IAsyncEnumerable<T> AsAsyncEnumerable(
    CancellationToken cancellationToken = default);

Task<T?> FirstOrDefaultAsync();
Task<T?> LastOrDefaultAsync();
Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate);
Task<T?> LastOrDefaultAsync(Expression<Func<T, bool>> predicate);

Task<int> CountAsync();
```

`CountAsync()` counts the whole table. Materialized queries apply requested ordering; progressive enumeration does not promise final arrival order.

`Where(...)` returns `IMagicQueryStaging<T>`, which intentionally does not expose `OrderBy`. Use `Cursor(predicate).OrderBy(...)` for one browser-side filtered and ordered chain, or materialize the `Where` result and order it in .NET.

Writes:

```csharp
Task AddAsync(T record, CancellationToken cancellationToken = default);
Task AddRangeAsync(IEnumerable<T> records, CancellationToken cancellationToken = default);

Task<int> UpdateAsync(T item, CancellationToken cancellationToken = default);
Task<int> UpdateRangeAsync(IEnumerable<T> items, CancellationToken cancellationToken = default);

Task DeleteAsync(T item, CancellationToken cancellationToken = default);
Task<int> DeleteRangeAsync(IEnumerable<T> items, CancellationToken cancellationToken = default);

Task ClearTable();
```

`AddAsync` and `AddRangeAsync` do not return generated primary keys or assign an auto-incremented key to the supplied objects. Updates and deletes require populated primary keys.

`UpdateAsync` is update-only and returns `0` or `1`. `UpdateRangeAsync` currently uses bulk-put/upsert behavior and returns the input count after success. `DeleteRangeAsync` likewise returns the number of requested keys, not a count of keys proven to have existed. Range operations do not have a documented all-or-nothing guarantee. See [writes, bulk operations, and transactions](writes-and-transactions.md).

## Staged query interfaces

The return type narrows the operations that can legally follow:

| Current interface | Notable next operations |
|---|---|
| `IMagicQuery<T>` | `Where`, `Cursor`, order, pagination, execution, CRUD |
| `IMagicQueryStaging<T>` | Additional `Where`, `Take`, `TakeLast`, `Skip`, parameterless first/last, execution |
| `IMagicQueryOrderable<T>` | `Take`, `TakeLast`, `Skip`, parameterless first/last, execution, `WhereAsync` |
| `IMagicQueryOrderableTable<T>` | Everything on `IMagicQueryOrderable<T>` plus predicate overloads of first/last |
| `IMagicQueryPaginationTake<T>` | `Skip`, execution, `WhereAsync` |
| `IMagicQueryFinal<T>` | Execution and in-memory `WhereAsync` |
| `IMagicCursor<T>` | Additional `Cursor`, order, `Take`, `TakeLast`, `Skip`, `StableOrdering`, parameterless first/last, execution |
| `IMagicCursorStage<T>` | `Take`, `TakeLast`, `Skip`, parameterless first/last, execution |
| `IMagicCursorPaginationTake<T>` | `Skip` and execution |
| `IMagicCursorSkip<T>` | Parameterless first/last and execution |
| `IMagicCursorFinal<T>` | Parameterless first/last and execution |

```csharp
Task<IEnumerable<T>> WhereAsync(
    Expression<Func<T, bool>> predicate);
```

`WhereAsync` on a final/orderable query materializes first and applies the predicate in .NET. It is not translated into IndexedDB.

`StableOrdering()` is available only on `IMagicCursor<T>` before the cursor moves to a later stage. It asks the cursor engine to retain deterministic internal ordering where that contract is documented; it does not change the progressive-stream arrival-order limitation.

## Database scope

```csharp
Task OpenAsync();
Task CloseAsync();
Task DeleteAsync();
Task<bool> IsOpenAsync();
Task<bool> DoesExistAsync();
```

The API manages one explicit database scope at a time. There are no public multi-database or all-database overloads.

`IsOpenAsync()` describes the current service instance's cached connection, not every browser tab. `DeleteAsync()` requests destructive deletion but does not return a detailed deletion result. See [browser support, storage, and multiple tabs](browser-support-and-storage.md).

## Storage estimate

`QuotaUsage` exposes:

```csharp
long Quota { get; }
long Usage { get; }
double QuotaInMegabytes { get; }
double UsageInMegabytes { get; }
(double quota, double usage) InMegabytes { get; }
```

## Schema contracts

`IMagicRepository` is a marker interface used during discovery.

`IMagicTableBase` requires:

```csharp
string GetTableName();
List<IMagicCompoundIndex>? GetCompoundIndexes();
IMagicCompoundKey GetKeys();
IndexedDbSet GetDefaultDatabase();
```

`IMagicTable<TDbSets>` additionally requires:

```csharp
TDbSets Databases { get; }
```

`MagicTableTool<T>` supplies protected helpers:

```csharp
CreatePrimaryKey(...)
CreateCompoundKey(...)
CreateCompoundIndex(...)
```

## Advanced serialization helpers

`MagicSerializationHelper`, `MagicJsonSerializationSettings`, `ITypedArgument`, and `TypedArgument<T>` are public for compatibility and testing. They support Magic's persisted-name mappings, constructor materialization, custom `System.Text.Json` options, nested collection shapes, and stream serialization.

Normal applications should use `IMagicIndexedDb`; the typed-argument and JavaScript envelope types are interop infrastructure, not a requirement for ordinary database access.

Settings supplied directly to `MagicSerializationHelper` do not globally configure the injected `IMagicIndexedDb` service. See [serialization and persisted types](serialization.md#custom-converters-and-advanced-helpers).

## Obsolete implementation paths

Concrete implementation classes still contain obsolete raw database/schema override methods for compatibility and unfinished migration work. They are intentionally absent from `IMagicIndexedDb`. Do not cast the injected service to reach implementation details; those paths are not the stable consumer contract.
