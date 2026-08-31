# Database management

Querying a table causes Magic IndexedDB to create or open its declared database as needed. Most applications can let that lifecycle happen automatically.

## Select a database

Use the table's default database:

```csharp
IMagicQuery<Person> people = await MagicDb.Query<Person>();
```

Or choose one of the table's declared databases through its strongly typed selector:

```csharp
IMagicQuery<Person> employees = await MagicDb.Query<Person>(
    person => person.Databases.Employee);
```

The older raw `IndexedDbSet` and name/schema override query paths are not part of the supported `IMagicIndexedDb` surface. Build normal application code around the two typed overloads above.

## Manage one database

`Database` returns an `IMagicDatabaseScoped` for exactly one `IndexedDbSet`:

```csharp
var database = await MagicDb.Database(IndexedDbContext.Client);

bool exists = await database.DoesExistAsync();
bool isOpen = await database.IsOpenAsync();

await database.OpenAsync();
await database.CloseAsync();
```

Closing releases the current Dexie connection. A later query can open the database again.

Delete a database only when permanent data loss is intended:

```csharp
await database.DeleteAsync();
```

`DeleteAsync()` requests removal of the browser database and all of its object stores and records. A future query may create a new empty database from the current schema. Other tabs can block deletion, and the current browser helper does not return a detailed deletion result, so call `DoesExistAsync()` afterward when confirmation matters.

The current API does not expose a public multi-database overload, parameterless `Database()`, `CloseAll`, or `DeleteAll`. Call the single-database API for each explicit database your application owns if such coordination is required.

## Clear one table instead

To remove all rows from a table without deleting the database:

```csharp
IMagicQuery<Person> people = await MagicDb.Query<Person>();
await people.ClearTable();
```

## Check browser storage use

```csharp
QuotaUsage storage = await MagicDb.GetStorageEstimateAsync(cancellationToken);

long usageBytes = storage.Usage;
long quotaBytes = storage.Quota;
double usageMiB = storage.UsageInMegabytes;
double quotaMiB = storage.QuotaInMegabytes;
```

The values come from the browser's storage estimate and should be treated as an estimate, not as a reservation or guaranteed capacity.

When the browser estimate API is unavailable, the current implementation returns zero values. Storage is origin/profile scoped and can be cleared or evicted according to browser policy. See [browser support, storage, and multiple tabs](../reference/browser-support-and-storage.md) for the complete operational contract.

## Disposal

`IMagicIndexedDb` is registered as a scoped service. Its implementation closes cached database connections and disposes the JavaScript module when the scope is disposed. Application code normally should not dispose an injected scoped instance manually.
