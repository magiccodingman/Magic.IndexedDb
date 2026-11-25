# Public API reference

This page is a quick reference for the interfaces most applications use. For complete signatures, use IntelliSense or the package's XML documentation.

## Registration

```csharp
IServiceCollection AddMagicBlazorDB(
    BlazorInteropMode interoptMode,
    bool isDebug);

IServiceCollection AddMagicBlazorDB(
    long jsMessageSizeBytes,
    bool isDebug);

IServiceCollection AddMagicIndexedDbNumericStorage(
    params MagicNumericStorageOptions[] databases);
```

`BlazorInteropMode.WASM` uses a 15 MiB message limit. `BlazorInteropMode.SignalR` uses 31 KiB.

`new MagicNumericStorageOptions(databaseName)` selects `MagicNumericStorageMode.Lossless`
for that logical database. Unlisted databases remain `Legacy`. Configure a new
database or plan an explicit cutover using the [lossless numbers guide](../guides/lossless-numbers.md).

## `IMagicIndexedDb`

```csharp
ValueTask<IMagicQuery<T>> Query<T>();

ValueTask<IMagicQuery<T>> Query<T>(
    Func<T, IndexedDbSet> dbSetSelector);

Task<QuotaUsage> GetStorageEstimateAsync(
    CancellationToken cancellationToken = default);

ValueTask<IMagicDatabaseScoped> Database(
    IndexedDbSet indexedDbSet);

ValueTask<IMagicDatabaseScoped> Database(
    IndexedDbSet indexedDbSet, string instanceName);

Task<IAsyncDisposable> ObserveMigrationsAsync(
    Func<MagicMigrationEvent, ValueTask> observer,
    CancellationToken cancellationToken = default);
```

`T` must be a class implementing `IMagicTableBase` with a public parameterless constructor. `Query<T>()` selects the table's default database; the selector overload chooses one of its strongly typed database associations.

## `IMagicQuery<T>`

Table identity:

```csharp
string DatabaseName { get; }
string SchemaName { get; }
```

For a separately named physical instance, `DatabaseName` remains the logical
declaration name; the scope's manager routes operations to its bound instance. Use
migration inspection or event metadata when the physical name is needed.

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

`CountAsync()` counts the whole table. `ToListAsync()` applies the requested ordering. A progressive stream can yield records from separate query branches out of order.

`OrderBy` is not available after `Where(...)`. Use `Cursor(predicate).OrderBy(...)` for a browser-side filtered and ordered query, or load the `Where` result and sort it in .NET.

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

`UpdateAsync` is update-only and returns `0` or `1`. `UpdateRangeAsync` uses bulk-put/upsert behavior and returns the input count after success. These replacement calls abort their browser transaction on a write failure, preserving the original rows and rolling back earlier replacements in the same batch. Ordinary bulk inserts can still partially succeed, and cancellation does not undo a browser write. `DeleteRangeAsync` returns the number of requested keys, not a count of keys proven to have existed. See [adding, updating, and deleting records](writes-and-transactions.md).

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

`StableOrdering()` is available only on `IMagicCursor<T>` before another operation moves the query to a later stage. It keeps the cursor's stable scan order and does not make a progressive stream arrive in sorted order.

## Database scope

```csharp
Task OpenAsync();
Task CloseAsync();
Task DeleteAsync();
Task<bool> IsOpenAsync();
Task<bool> DoesExistAsync();
ValueTask<IMagicQuery<T>> Query<T>();
Task<JsonElement?> InspectMigrationAsync(CancellationToken cancellationToken = default);
Task RetryMigrationAsync(CancellationToken cancellationToken = default);
Task CancelMigrationAsync();
Task<JsonElement> RecoverMigrationAsync(string recoveryId, string sha256, CancellationToken cancellationToken = default);
```

The API manages one physical instance per scope. The two-argument `Database` overload
uses one registered logical schema for a separately named instance; scoped `Query<T>()`
validates table membership. There are no all-database operations on the public interface.
Obtaining a scope does not create storage; `OpenAsync` and queries initialize it.

`InspectMigrationAsync` returns metadata only without creating/upgrading a database.
`RecoverMigrationAsync` explicitly selects a recovery ID and descriptor digest
approved by the pinned migration manifest. The result identifies the committed
edge and the application's configured target. Use ordinary open to initialize
any later revisions. Its token cancels the .NET wait. Use
`CancelMigrationAsync` to cancel browser work. Recovery observations add
`RecoveryId`, `RecoveryPhase` and `VerifiedRecords`.
See the [recovery contract](../architecture/adr-partial-migration-recovery.md).

For authenticated histories, the existing encryption lifecycle authorizes managed
initialization and recovery. `InitializeAsync` explicitly establishes a new
identity; `EnsureUnlockedAsync` authorizes an existing one; `LockAsync` revokes
pending work. `MagicEncryptionOptions.PhysicalDatabaseName` is an optional explicit
binding for reviewed in-place enrollment and requires a pinned manifest. Keep
that account context after all fields become public. See
[encrypted migrations](../guides/encrypted-migrations.md).

`RetryMigrationAsync` retries failed initialization or resumes committed progress;
its token cancels the .NET wait. `CancelMigrationAsync` cancels the browser attempt
at a safe boundary. `ObserveMigrationsAsync` returns a disposable subscription for
`MagicMigrationEvent` (`Phase`, `Code`, `Revision`, `TargetRevision`, `MigrationId`,
`ProcessedRecords`, `DocumentationUrl`, and extensible metadata). See
[migrations](../guides/migrations.md).

`IsOpenAsync()` describes the current service instance's cached connection, not every browser tab. `DeleteAsync()` awaits destructive deletion and propagates failures; it does not return a structured deletion report. See [browser storage and multiple tabs](browser-support-and-storage.md).

## Storage estimate

`QuotaUsage` exposes:

```csharp
long Quota { get; }
long Usage { get; }
double QuotaInMegabytes { get; }
double UsageInMegabytes { get; }
(double quota, double usage) InMegabytes { get; }
```

## Schema interfaces

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

## Version 3 schema declarations and exporter

`IndexedDbSet` is an immutable, sealed declaration. `WithSchemaId`, `WithMigrations`,
`Include<T>`, `Exclude<T>`, `ExplicitTablesOnly`, and `IncludeDefaultTables` return a
new declaration. Default membership follows `GetDefaultDatabase()`; secondary
placements need an explicit include.

`MagicSchemaResolver.Resolve` and `ResolveDefinitions` produce `ResolvedMagicDatabase`
values. `MagicSchemaExporter` emits neutral `MagicSchemaDocument` JSON for the CLI.
`MagicSchemaOptions` carries numeric modes, protected database identities, and record
policies consistently between runtime and the exporter. `MagicSchemaJson` exposes
canonical serialization and fingerprints. `MagicStorageCodecAttribute` records a
versioned contract for opaque converters; `MagicSchemaException` has stable `Code`
and `DocumentationUrl` properties. See [schema authoring](../guides/migration-schema.md).

## Serialization helpers

`MagicSerializationHelper`, `MagicJsonSerializationSettings`, `ITypedArgument`, and `TypedArgument<T>` support stored-name mappings, constructor materialization, custom `System.Text.Json` options, nested collections, and stream serialization.

Normal applications should use `IMagicIndexedDb`; the typed-argument and JavaScript envelope types are interop infrastructure, not a requirement for ordinary database access.

Settings supplied directly to `MagicSerializationHelper` do not globally configure the injected `IMagicIndexedDb` service. See [custom converters](serialization.md#custom-converters).

## Avoid implementation classes

Some concrete classes still contain obsolete database and schema override methods. They are not part of `IMagicIndexedDb` and may disappear. Application code should use the injected interface instead of casting it to an implementation class.
