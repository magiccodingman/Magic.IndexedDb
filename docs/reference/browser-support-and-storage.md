# Browser support, storage, and multiple tabs

Magic IndexedDB runs inside the browser and inherits the browser's IndexedDB, origin, profile, and storage policies. The library can organize and query browser-local data; it cannot turn that storage into a server database or guarantee that the browser will retain it forever.

## Runtime requirements

The current package requires:

- A .NET 10 Blazor application
- Browser JavaScript interop after the application becomes interactive
- IndexedDB
- JavaScript module support
- Blazor streaming interop for Magic's streamed request and result transport

Prerendered components must wait until interactive rendering before calling Magic IndexedDB.

## Validated browser engines

Pull requests and `master` builds run the browser integration suite against:

| Engine target | CI coverage |
|---|---|
| Chromium | Full Playwright browser suite on Linux |
| Firefox | Full Playwright browser suite on Linux |
| WebKit | Full Playwright suite on Linux |
| WebKit on macOS | Database-open/registration plus CRUD and streaming suites |

This is an engine-level validation statement, not a promise about every branded browser, browser version, extension environment, or device. Playwright WebKit is not the branded Safari application, and the repository does not currently run real iPhone or iPad device tests.

Applications with a formal support matrix should run their own smoke tests against the exact browser versions and devices they advertise.

## Origin and profile scope

IndexedDB data belongs to the browser origin and user profile. As a result:

- Different origins do not share the database merely because they use the same database name.
- Development and production hosts commonly have separate data.
- Another browser or browser profile has separate storage.
- Clearing site data, resetting a profile, or uninstalling some installed web applications can remove the database.
- Private/incognito modes may isolate or discard data according to browser policy.

Do not use browser-local IndexedDB as the only durable copy of irreplaceable data unless the product explicitly accepts that risk and provides export, synchronization, or backup behavior.

## Quota and persistence

`GetStorageEstimateAsync()` returns the browser's `navigator.storage.estimate()` values when available. When that API is unavailable, the current browser helper returns zero for both quota and usage.

The values are estimates:

- `Quota` is not reserved capacity.
- `Usage` can include origin storage outside Magic IndexedDB.
- A successful estimate does not guarantee that a later write will fit.
- Browsers can apply eviction and persistence policies outside Magic's control.

Magic IndexedDB does not currently expose a storage-persistence request API. Applications that require persistent-storage negotiation must design and test that browser interaction separately.

## Connection state

`IsOpenAsync()` reports whether this Magic IndexedDB service instance currently has the named database open in its own JavaScript connection cache. It does not report connections held by another tab, iframe, worker, browser process, or application instance.

`CloseAsync()` closes and removes this instance's cached connection. It does not delete data. A later query can reopen it.

`DoesExistAsync()` is a best-effort browser existence check. Chromium can use the database-listing API; other paths probe IndexedDB without intentionally retaining a newly created database. Unexpected browser errors or a fallback timeout are reported as `false`, so the result should not be treated as proof that storage is globally accessible and healthy.

## Deleting a database

`DeleteAsync()` requests deletion of the complete named browser database. Successful deletion removes all of its object stores and records, and a later query may create an empty database from the current schema.

Other open contexts can block database lifecycle operations. Coordinate destructive deletion with every application tab and verify the result with `DoesExistAsync()` when confirmation matters. The current browser deletion helper logs deletion failures and does not provide a detailed deletion result through the public method, so the completed `Task` alone is not a strong verification contract.

Never delete a database automatically as a generic response to an unknown open, query, or migration error.

## Multiple tabs and schema changes

Multiple tabs can concurrently open the same origin/database. Ordinary IndexedDB coordination still applies:

- An older open connection can delay or block a version change or deletion.
- `IsOpenAsync()` in one tab cannot inventory other tabs.
- Closing one injected service does not close another tab's connection.
- A schema rollout should tolerate old and new application versions overlapping during deployment.

The current release does not expose an application-level cross-tab migration coordinator. Test schema changes with at least two tabs and realistic existing data. See [schema evolution](../guides/schema-evolution.md).

## Schema discovery across databases

At startup, the current factory discovers every loaded `IMagicRepository` database set and every loaded `IMagicTable<TDbSets>` table. It supplies the full discovered table-schema collection when creating each discovered database.

The table's `Databases` selector controls which database names application code can choose conveniently; it is not currently a schema-pruning or authorization mechanism. Avoid assuming that an unselected table schema is absent from another discovered Magic database, and use separate origins or an actual authorization layer for security boundaries.

## Production checklist

- Test on every browser/device in the product's declared support matrix.
- Test offline startup and reconnect behavior.
- Test quota failure and user-initiated site-data clearing.
- Test multiple tabs during a deployment that changes persisted models.
- Provide export/synchronization for irreplaceable offline data.
- Treat database deletion as a destructive user-visible action.
