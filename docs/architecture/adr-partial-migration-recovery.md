# Recovery from a partially committed migration

**Status** Implemented in the 3.0 preview for the bounded topology below. Stable
acceptance requires implementation review and application-specific rollout evidence.

## Boundary

An application explicitly selects an approved recovery for one interrupted
resumable custom transform on one store. The original edge preserves
native layout, primary keys, numeric format and existing field representations.
Unrelated store contracts must match. A package may handle added or removed fields
on the affected store. Structural work, multiple operations, and record-codec
changes require another recovery strategy. The authenticated extension supports
source/target protection changes on the affected store under the same data key;
it decodes each partition using its original policy and encrypts repair output
under the target policy. See the [authenticated contract](adr-authenticated-migrations.md).

For the affected store, an omitted `recordCodec` and an explicit JSON
`null` both mean there is no record codec. The CLI and browser compare
them as equivalent for recovery eligibility. Each schema snapshot keeps
its original representation and fingerprint.

The application supplies deterministic repair and verification functions. The
engine owns native cursors, transactions, checkpoints, readiness and lineage.
Application hooks must avoid external side effects. If required original
information was lost, the repair or verifier must fail and leave access blocked.
Retain the database and use application-specific support or export. Ordinary
later revisions cannot rescue a still-interrupted edge.

## Versions and immutable assets

Existing schema and migration documents remain protocol **1**. A manifest with
recoveries is protocol **2** and requires `resumableRepairV1`. It keeps the
original schema and migration reference arrays intact and adds bindings:

```json
{
  "id": "repair-0021-invoice-tax",
  "fromRevision": 20,
  "path": "recoveries/repair-0021-invoice-tax/recovery.json",
  "sha256": "sha256:…"
}
```

There is one approved binding per original edge. The manifest, pinned by the
application, gives the recovery document's URL. The explicit request supplies
its ID and exact digest, never an alternate URL or private checkpoint. The
canonical recovery document is protocol **2**:

```json
{
  "protocolVersion": 2,
  "id": "repair-0021-invoice-tax",
  "schemaId": "invoices",
  "source": {
    "revision": 20,
    "schemaHash": "sha256:…",
    "migrationId": "0021_ApplyTax",
    "migrationHash": "sha256:…"
  },
  "target": { "revision": 21, "schemaHash": "sha256:…" },
  "operation": { "index": 0, "store": "invoices" },
  "module": { "path": "repair.mjs", "sha256": "sha256:…" }
}
```

The module exports `repairProcessed`, `repairUnprocessed`, and `verify`.
Digests are full SHA-256 values in published assets. The engine verifies the
original edge, models, original module bytes, approved descriptor, and repair
module before admission. The original module is retained without evaluating or
re-running it as part of recovery. The CLI seals recovery assets and bindings
using the same publication lock as original history.

## Journal and transactions

A v1 journal retains its existing meaning even under a v2 manifest. Ordinary recovery
admission bumps the native database version and writes a **v2** journal with
status `recovering`, the original `active` checkpoint and a separate
`recovery` checkpoint. A v1 engine rejects a v2 journal. A cooperating handle
that was already open detects the native version change. Authenticated databases
retain **v3** journals, with a separately authenticated complete journal and an
active policy fence. Their private recovery audit records are encrypted.

| State | Durable action |
| --- | --- |
| `migrating` at the committed revision | Verify the source journal, installed layout and numeric policy, then admit only the requested approved binding. |
| `recovering`, `repair` phase | Preserve the original boundary. The inclusive prefix calls `repairProcessed` and the untouched suffix calls `repairUnprocessed`. An empty boundary means the entire store is unprocessed. An exhausted original cursor means all records were processed. The engine validates native key/count consistency and commits batch records and progress in one transaction. |
| `recovering`, `verify` phase | Scan the entire store again using a separate cursor and require the verifier to return exactly `true` for each row. Business verification belongs to the application. The engine validates progress, keys, layout, numeric policy and journal identity. |
| `ready` at the original edge target | Commit readiness and append a recovered outcome retaining the original edge digest. Keep the original and recovery checkpoints in a private audit record. Future manifests must retain the approved binding. Later revisions keep journal v2 for ordinary databases or v3 for authenticated databases, and preserve the recovered outcome. |

For example, recovered history contains:

```json
{
  "id": "0021_ApplyTax",
  "sha256": "sha256:…",
  "outcome": "recovered",
  "recovery": { "id": "repair-0021-invoice-tax", "sha256": "sha256:…" }
}
```

Cancellation, hook failures and transaction failures preserve committed recovery
batches. Only the same admitted package can resume. Its verifier continues after
the last committed verification checkpoint. A permanent verifier failure leaves
the barrier in place. There is no second repair, checkpoint editor or automatic
reset for an unsuccessful recovery in preview.

`alreadyCompletedOriginal` requires an original edge entry in validated history.
A bootstrap at a later target does not prove the original edge ran.
`alreadyRecovered` also verifies the retained binding. Both outcomes are
read-only and avoid evaluating application hooks. A later ordinary revision can
normalize data across successful original and recovered outcomes.

## Explicit invocation

```js
const result = await engine.recoverDatabase({
  name, schema, manifestUrl, recovery: { id: recoveryId, sha256: pinnedDigest }, signal, onEvent
});
```

```csharp
await using var observation = await magic.ObserveMigrationsAsync(ObserveMetadata);
var db = await magic.Database(InvoiceDatabases.Local);
var result = await db.RecoverMigrationAsync(recoveryId, pinnedDigest, cancellationToken);
await db.OpenAsync();
```

The result gives `outcome`, `recoveryId`, `revision` and
`applicationTargetRevision`. Successful explicit recovery commits only the
repaired edge. An open targeting revision 24 still runs and checks the ordinary
path through revision 24. Normal opening never selects a repair. The C# token
cancels the .NET wait. Call `CancelMigrationAsync` to abort browser work.

Inspection and `recovering`, `recoveryCheckpoint`, `recovered` and failure
events expose metadata. They omit durable cursor keys, rows and hook exception
messages. `MAGDB_RECOVERY_REQUIRED` calls for an explicit action,
`MAGDB_RECOVERY_PACKAGE_REQUIRED` calls for the admitted bytes,
`MAGDB_RECOVERY_BINDING_REQUIRED` calls for a compatible deployment, and hook or
verifier failure calls for developer diagnosis.

The [released upgrade runner](../contributing/migration-testing.md) includes the
[CLI-authored Blazor worked example](../../tests/migrations/RecoveryWasm). It
persists one failing migration batch, explicitly cancels/resumes recovery and
then opens the later ordinary revision. The portable browser suite exercises the
same runner independently of the .NET adapter.
