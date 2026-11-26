# Magic IndexedDB 3.0 implementation contract

This document coordinates the initial implementation. Public commands and APIs are
documented separately once validated. Package ID: `Magic.IndexedDb.Cli`; command:
`magicdb`. Runtime and CLI target `3.0.0-preview.1`. The independent protocol is 1.

## Ownership and invariants

- C# resolves table membership and persisted record metadata. One resolver serves
  runtime and design-time export. Existing query/CRUD APIs stay intact.
- Default membership uses `GetDefaultDatabase()`. Explicit includes/excludes and
  explicit-only membership are resolved once. All-tables deployment is not retained.
- A schema ID identifies a history; a physical name identifies one instance.
- The planner consumes neutral JSON. The browser engine never loads .NET.
- No undeclared drops, automatic adoption of unknown data, history rewriting,
  swallowed failures, or success markers before transaction completion.
- Plain new databases may bootstrap a revision-1 inline model through the same
  engine. Existing model changes require an explicit migration package. This is
  not the legacy schema deployment/repair path.
- Every event is metadata-only (no records, credentials, or encryption keys).
  Observer errors cannot change transaction outcomes. Recovery offers inspect,
  retry/resume and export diagnostics; destructive reset requires explicit caller
  action and is never an error fallback.

## Neutral JSON contract (camelCase)

`SchemaDocument`:

```json
{
  "protocolVersion": 1,
  "schemaId": "clients",
  "numericFormat": "legacy",
  "stores": [{
    "name": "clients",
    "keyPath": "id",
    "autoIncrement": false,
    "indexes": [{"name":"name","keyPath":"name","unique":false,"multiEntry":false}],
    "fields": [{"name":"id","type":"integer32","nullable":false,"codec":"json"}]
  }],
  "requiredCapabilities": []
}
```

keyPath is a string, ordered string array, or null. Store/index lists and field
lists are name-sorted by the exporter. Field kinds/codec IDs are portable, not CLR
assembly names. Protection, when present, is explicit per store as `encryptedFields`
and `exposedFields` (null means no whole-record allowlist). No key material is serialized.
Unknown required capabilities/operations fail before changing any database.

Canonical JSON recursively sorts object keys ordinally, preserves array order,
and uses JSON scalar encoding. Asset SHA-256 is over published UTF-8 bytes, lower-case
hex prefixed `sha256:`. Schema fingerprints use canonical JSON UTF-8. The runtime
and CLI link `shared/Magic.IndexedDb.Protocol/CanonicalJson.cs`; its explicit string
and number writer matches JSON.stringify, including Unicode and exponent boundaries.

`manifest.json`:

```json
{
  "protocolVersion": 1,
  "schemaId": "clients",
  "targetRevision": 2,
  "requiredCapabilities": [],
  "schemas": [
    {"revision":1,"path":"schemas/0001.json","sha256":"sha256:..."},
    {"revision":2,"path":"schemas/0002.json","sha256":"sha256:..."}
  ],
  "migrations": [
    {"id":"0002_AddStatus","fromRevision":1,"toRevision":2,"path":"migrations/0002_AddStatus.json","sha256":"sha256:..."}
  ],
  "baselines": []
}
```

A baseline is `{id, schema: {path, sha256}, revision}`; schema describes the
accepted OLD layout, revision names its matching managed model. Adoption must
match the full physical layout; model/codec provenance remains declared by the
developer. Extras require an explicitly described transition, never inferred drops.

`MigrationDocument`:

```json
{
  "protocolVersion":1,"schemaId":"clients","id":"0002_AddStatus",
  "fromRevision":1,"toRevision":2,
  "fromSchemaHash":"sha256:...","toSchemaHash":"sha256:...",
  "mode":"atomic","requiredCapabilities":[],
  "operations":[{"op":"createIndex","store":"clients","index":{"name":"status","keyPath":"status","unique":false,"multiEntry":false}}]
}
```

Operations: `createStore` with `definition` (a store descriptor), `deleteStore`
with `store` and `allowDataLoss:true`, `createIndex` with store/index, `deleteIndex`
with store/name, `renameStore` with store/to, `renameField` with store/from/to,
`setMissing` with store/path/value, `deleteField` with store/path/allowDataLoss:true,
`transform` with store/module/exportName (default transform)/sha256. An unresolved
planner operation is `manual` and prevents validation/publishing/runtime preflight.
Module URLs are relative to the migration JSON. Built-in transforms touch only
safe top-level fields initially. Custom transforms operate historical records and
return the replacement record; engine owns writes/checkpoints. No eval.

Atomic operations have synchronous record hooks (native requests may be chained).
Resumable migrations are data-only initially, one or more per-store transforms,
with durable operation index and last source primary key; key-changing hooks are
rejected until an explicit staging operation is supported. Schema and data chunks
are never claimed to share one all-or-nothing rollback. Unknown extension fields
are permitted only if non-semantic; requiredCapabilities guards new semantics.

## Browser module API

`wwwroot/migrations/migrationEngine.js` exports:

- `ensureDatabase({name, schema, manifestUrl?, manifest?, onEvent?, signal?, batchSize?})`
  -> `{name, schemaId, revision, schemaHash, nativeVersion}` after completion.
- `inspectDatabase(name)` -> metadata and physical schema, with no database creation.
- `subscribeMigrationEvents(listener)` -> unsubscribe; global metadata-only observer.
- `MigrationError` with code/details/retryable and a documentation URL.

Manifest schema and expected runtime schema must match before writes. Runtime
database opening goes through this promise before creating a dynamically opened
Dexie handle. Native upgrade transactions, locks, durable metadata and per-request
readiness checks govern execution. Preload modules before opening transactions.
Protected databases cannot be mutated through the plain engine; authenticated
storage integration is required and unsupported transitions fail explicitly.

## Tooling and release scope

The CLI supports schema export, database listing, migration add/validate/publish,
baseline planning and diagnostics. --schema accepts a neutral file directly;
--project invokes an explicit design-time executable exporter in a separate process
under the target SDK (never boots the Blazor application). A runnable companion
project can call the public exporter. Publishing emits static deterministic files.
No NuGet push, merge, tag, or GitHub release is part of this PR. Release publishing
credentials and activation belong to the user. CI must test installed nupkg tools.

The implementation handoff must distinguish supported behavior, tested evidence,
and limitations requiring further validation; no claims of complete historical or
protected migration support without executed tests.
