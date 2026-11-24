# Browser storage and multiple tabs

Magic IndexedDB stores data in the browser's IndexedDB database. That means the data belongs to the current site and browser profile.

The same database name on two different domains refers to two different databases. Development and production sites also have separate data, as do different browsers and browser profiles.

Users can remove this data by clearing site storage. Private browsing may discard it when the private session ends. If the data cannot be replaced, give users a way to sync or export it.

## Storage space

Use `GetStorageEstimateAsync()` to ask the browser how much storage the site is using:

```csharp
QuotaUsage storage = await MagicDb.GetStorageEstimateAsync(cancellationToken);

long usedBytes = storage.Usage;
long availableBytes = storage.Quota;
```

These numbers are estimates. They can include other storage used by the same site, and available quota can change. If the browser does not support storage estimates, Magic returns zero for both values.

A storage estimate does not reserve space. A later write can still fail because the browser is out of space or has changed its storage policy.

## Open and closed databases

`IsOpenAsync()` checks whether the current Magic IndexedDB service has that database open. It cannot see connections owned by another tab.

`CloseAsync()` closes the current connection without deleting any data. Magic opens it again the next time it is needed.

`DoesExistAsync()` checks whether the browser can find the database. Some browsers do not provide a direct database-listing API, so Magic falls back to probing IndexedDB. Browser errors or a timeout can produce `false` even when the underlying problem is not simply “database missing.”

## Deleting a database

```csharp
var database = await MagicDb.Database(IndexedDbContext.Client);
await database.DeleteAsync();
```

This deletes every table and record in that database. Other open tabs can block the deletion. If your UI needs to confirm that deletion finished, close the application's other tabs or connections and then call `DoesExistAsync()`.

Do not automatically delete the database because a query or upgrade failed. Browser-local data may be the user's only offline copy.

## Multiple tabs

Each tab has its own connection. Closing the database in one tab does not close it in another.

An older tab can also block a schema change or database deletion. Test schema updates with two tabs open, especially when users may leave the application running during a deployment.

## Which tables are created

Magic discovers all `IMagicRepository` and `IMagicTable<TDbSets>` types loaded by the application. Right now, it gives every discovered Magic database the full set of discovered table schemas.

The `Databases` property gives you strongly typed choices when querying a table, but it does not limit which object stores Magic creates. It is also not a security boundary.
