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

Magic discovers registered `IMagicRepository` and `IMagicTable<TDbSets>` types, then resolves membership through each table's `GetDefaultDatabase()` plus repository includes/excludes. Prefer explicit type registration for predictable app/project boundaries. An explicit-only declaration uses just its included types.

The `Databases` property gives strongly typed query choices; the chosen database must actually include that table. Membership is a schema contract, not a security boundary. See [v3 schema declarations](../guides/migration-schema.md).

## Managed migration requirements

Version 3 requires IndexedDB, Web Crypto, ES modules, and Web Locks in a secure
context. Unknown capabilities fail before writes. The engine coordinates migration
ownership with a Web Lock and uses native upgrade transactions to invalidate older
connections. A `blocked` event lets the application ask users to close/reload old
tabs. A stale managed handle fails rather than continuing with a mismatched model.

Static manifest and asset URLs are pinned per app release. Offline upgrades require
all needed assets to be cached; Magic does not install or manage a service worker.
Custom transform modules execute verified bytes as Blob modules and need CSP `blob:`
permission. Read [migration deployment and recovery](../guides/migrations.md).
