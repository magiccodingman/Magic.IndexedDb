# Managed browser migrations

Magic IndexedDB 3 introduces a static migration protocol. C# defines the persisted
model; the CLI records reviewed changes; a JavaScript engine advances each local
database independently. The engine has no .NET, Blazor, REST API, or server runtime
dependency. The existing Razor class library imports it as an ES module, so Blazor
applications do not add a script tag to `index.html`.

This branch is `3.0.0-preview.1`. See [validation and release acceptance](../contributing/migration-testing.md)
before deploying it to irreplaceable browser data. Protected migration adapters,
key-changing resumable migrations, and numeric codec cutovers are explicitly
unsupported in this preview; the engine rejects them instead of bypassing storage
protections. Existing authenticated encrypted CRUD remains available.

## The three identities

| Identity | Owner | Meaning |
|---|---|---|
| `schemaId` | Application declaration | Stable logical database model and history |
| Physical name | Application instance | Independent browser storage and migration progress |
| Revision | CLI history | Ordered application schema revision, starting at 1 |

NuGet's version and `protocolVersion` are different again. The initial migration
protocol is version 1. A package upgrade does not itself change application data.
IndexedDB's native version is allocated by the engine for structural transitions;
it is not an application schema revision. A legacy native version of 10 may be
adopted at logical revision 1 and advanced through native version 11.

## Declare a model and pin its package

```csharp
public sealed class AppDatabases : IMagicRepository
{
    public static readonly IndexedDbSet Clients = new IndexedDbSet("Clients")
        .WithSchemaId("clients")
        .WithMigrations("db/releases/app-003/clients/manifest.json");

    public static readonly IndexedDbSet Audit = new IndexedDbSet("Audit")
        .WithSchemaId("audit")
        .ExplicitTablesOnly()
        .Include<AuditRecord>();
}
```

`GetDefaultDatabase()` places a discovered table in its default database.
`Include<T>()` adds another placement; `Exclude<T>()` removes a discovered default;
`ExplicitTablesOnly()` turns off default discovery for that declaration. These are
inputs to one resolver and one migration protocol. There are no separate automatic
and explicit runtime migration systems. See [schema declarations](migration-schema.md).

A new empty database can bootstrap at revision 1 without a manifest. Its model
fingerprint is still recorded. Later model changes fail until a corresponding
migration package is provided. A database created before v3 requires an explicit
adoption baseline even when its layout appears to match.

For a code-first workflow, use the [CLI guide](migration-cli.md). In a release build:

```sh
magicdb migrations validate --project tools/App.Schema/App.Schema.csproj --all --json
magicdb migrations publish --project tools/App.Schema/App.Schema.csproj --all --output wwwroot/db/releases/app-003
```

The exporter is a small opt-in console project. It shares models and the resolver
with the application; it does not start the Blazor application. The neutral JSON
input is equally valid, so another language can produce the same contract.

## Open an instance and query it

Ordinary `await MagicDb.Query<Person>()` still works. Only the database needed by
the query is initialized, and queries are returned after its migration succeeds.

For several instances of one model:

```csharp
var tenant = await MagicDb.Database(AppDatabases.Clients, "tenant-42-clients");
IMagicQuery<Person> people = await tenant.Query<Person>();
await people.AddAsync(new Person { Name = "Ada" });
```

The instance has the same logical schema and static assets, with independent rows,
revision, history, and checkpoints. Different schemas cannot share one live physical
instance. Account-bound encrypted storage continues to use encryption contexts
rather than the plain instance-name overload.

Getting a database scope does not create or migrate it. `DoesExistAsync()` and
`InspectMigrationAsync()` can inspect an existing installation before opening it.
`OpenAsync()` or scoped `Query<T>()` starts initialization. Queries for a table
outside the resolved membership fail before database creation.

## What happens during an upgrade

The engine pins the model and manifest inputs, obtains an exclusive Web Lock for
the physical database, and checks the installed journal and native schema. It
validates the target model and every needed intermediate schema, migration, and
custom module before the first write. Missing assets, hash mismatches, unsupported
capabilities, unresolved manual steps, and gaps fail in preflight.

For a fresh installation it creates the current target schema directly. Historical
record transformations are unnecessary for an empty database. Fresh bootstrap does
not run seed hooks: if the application needs reference rows, initialize them through
an application-owned, idempotent process after readiness. Do not hide required seed
data exclusively in a historical transform.

For an existing installation it follows the ordered path from its committed
revision. Each atomic revision commits independently. If revision 2 succeeds and
revision 3 fails, retry starts from revision 2; successful transformations are not
replayed. The entire multi-revision upgrade is not one transaction.

### Atomic migrations

Structural changes, synchronous field changes, and synchronous custom record
transforms run in one native IndexedDB upgrade transaction with the new journal
entry. A failed unique index or transform aborts the revision, including its
structural changes and journal update. Atomic hooks must not return a Promise or
perform network work. Keep atomic migrations short enough for the target browsers.

### Resumable migrations

Protocol v1 supports data-only batches with stable primary keys. A native version
change first marks the database unavailable to ordinary work. Each batch transforms
records, then commits both the writes and its checkpoint in one transaction. After
a crash, an interrupted batch may be evaluated again but committed batches are not
applied again. Hooks therefore must avoid external side effects and derive results
from the source record and stable configuration.

Async transforms are supported in resumable mode. Cancellation can stop waiting
for a pending hook and prevents its late result from committing; it cannot terminate
arbitrary JavaScript or reverse external requests made by custom code. A synchronous
infinite loop still blocks the browser. `batchSize` is a JavaScript engine option
between 1 and 10,000, default 256.

Structural changes must be separate atomic revisions. Resumable primary-key changes,
staging-store swaps, encryption transitions, and numeric codec conversion require
future explicitly negotiated capabilities. They do not silently fall back to raw
record copying.

## Custom transformations

Scaffolding creates unresolved `manual` operations when it cannot infer meaning,
including ambiguous renames and representation changes. Review the draft and replace
those operations with deliberate built-ins or a custom transform. Publish and
validation reject unresolved operations.

```js
// Self-contained ES module; atomic mode requires a synchronous return.
export function transform(record, context) {
    return { ...record, displayName: record.givenName + " " + record.familyName };
}
```

The operation declares `module`, `sha256`, and optionally `exportName`. Module paths
are resolved relative to their migration document. Compute a digest with
`magicdb hash path/to/transform.js`. Both data changes and intended target schema
must be reviewed; a schema descriptor cannot prove business invariants such as
balanced account totals or valid domain transitions.

The engine verifies the exact module bytes and imports those bytes as a Blob ES
module. Custom hooks need CSP permission for `blob:` scripts. Modules must be
self-contained; relative imports have no stable HTTP base inside the Blob. Custom
modules are application code and must be reviewed just like any other shipped
JavaScript. Schema-only and built-in migrations need no custom-module import.

## Observe, inspect, cancel, and resume

```csharp
await using var observation = await MagicDb.ObserveMigrationsAsync(notification =>
{
    // Forward only the metadata your application wants to collect.
    logger.LogInformation("Database migration {Phase}: {Code}",
        notification.Phase, notification.Code);
    return ValueTask.CompletedTask;
});

var database = await MagicDb.Database(AppDatabases.Clients);
var snapshot = await database.InspectMigrationAsync(cancellationToken);

await database.OpenAsync();
// A UI may invoke this while OpenAsync/Query is pending:
await database.CancelMigrationAsync();
// After addressing the failure:
await database.RetryMigrationAsync(cancellationToken);
```

Events include `preflight`, `blocked`, `adopting`, `migrating`, `checkpoint`, `ready`,
and `failed`. They carry schema identity, physical name, revision/progress, and stable
error codes when applicable. They do not contain records, record keys, encryption
keys, or arbitrary custom exception messages. Applications should decide whether
physical names are appropriate for their telemetry. No telemetry is sent by Magic.

Observers do not control a transaction and observer exceptions cannot turn a commit
into a failure. Public inspection returns metadata and native layout; an active
checkpoint exposes `hasCheckpoint`, never its private record key. Do not edit the
reserved `__magic_migrations__` journal to force an upgrade through.

`CancelMigrationAsync()` cancels the current tab's attempt. Committed batches stay
committed; an active atomic transaction is aborted when cancellation reaches it.
The cancellation token on `RetryMigrationAsync` bounds the .NET wait; explicitly
call `CancelMigrationAsync` to cancel browser work as well. A blocked native open
cannot be removed from the browser's queue, so its handlers remain attached and
abort it if it becomes runnable after cancellation.

### Emergency recovery decisions

| Situation | Safe application response |
|---|---|
| Another tab blocks an upgrade | Show a close/reload prompt for the older tab; cancel this attempt if the user declines |
| Missing or mismatched static bytes | Restore the exact immutable release assets; retry the same package |
| Incomplete resumable migration | Restore its original package and resume from its journal |
| Database is newer than the running app | Load a compatible newer app; do not downgrade the database |
| Unknown legacy layout | Inspect without mutation and prepare a supported baseline/explicit transition |
| Quota exhaustion | Free unrelated replaceable data or offer export; retry after space is available |
| Deterministic transform/data error | Preserve data and diagnostics; reproduce with the original history and a representative fixture |

Already committed history is immutable. Fix a completed migration with a new forward
revision. Replacing a partially executed resumable module under the same migration
ID is refused because different clients could have applied different logic. This
preview does not expose a force-complete, rewrite-checkpoint, automatic rollback, or
automatic reset API. Repairing a genuinely faulty partially committed migration
requires an application-specific recovery design and validation, not a silent hash
override. Keep that scenario in the release acceptance review.

`DeleteAsync()` remains an explicit destructive administrative action. It is never
called by migration failure handling.

## Static deployment and offline use

Deploy assets to an immutable release directory and compile the corresponding
manifest URL into the application. All assets must share the manifest origin;
the manifest itself must use the application's origin. Deploy the complete asset
set before publishing the app that references it. Retain old packages for offline
or long-absent clients and for incomplete resumable work.

The engine checks bytes even when an HTTP or service-worker cache serves them. An
upgrade while offline succeeds only when all required static assets are already
available. Magic does not install a service worker, evict caches, reload tabs, or
select an application deployment policy for you. Retain history references; this
preview does not support pruning a committed chain through an implicit checkpoint.
Fresh clients fetch the target schema rather than all historical migration bodies.

## Diagnostics

`MigrationError` carries `code`, `details`, `retryable`, and `documentationUrl` in
JavaScript. Blazor surfaces the code in the interop exception and emits the structured
failure event. An application can choose its own retry UI and telemetry destination.

Typical codes are `MAGDB_MODEL_DRIFT`, `MAGDB_ADOPTION_REQUIRED`,
`MAGDB_LEGACY_SCHEMA_UNKNOWN`, `MAGDB_ASSET_HASH_MISMATCH`, `MAGDB_HISTORY_REWRITTEN`,
`MAGDB_DATABASE_NEWER`, `MAGDB_DATABASE_INCOMPATIBLE`, `MAGDB_BLOCKED`,
`MAGDB_CANCELLED`, `MAGDB_QUOTA_EXCEEDED`, and `MAGDB_RESUME_PACKAGE_REQUIRED`.
Unsupported protected migrations use `MAGDB_PROTECTED_MIGRATION_UNSUPPORTED` in the
Blazor adapter and `MAGDB_PROTECTED_ADAPTER_REQUIRED` at the plain engine boundary.

See [the protocol](../architecture/migration-protocol.md), [CLI diagnostics](migration-cli.md#diagnostics),
and [the v3 upgrade guide](../upgrading/v3.md) for the other validation boundaries.
