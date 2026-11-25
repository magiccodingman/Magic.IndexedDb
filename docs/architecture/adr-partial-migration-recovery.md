# ADR proposal for recovery from a partially committed migration

**Status** Proposed. Resumable migration stays in preview. Stable acceptance requires review of this contract and passing acceptance evidence. This PR changes failure diagnostics and retains the existing guards.

## Decision

Provide explicit recovery of one interrupted resumable edge, under a separately identified and pinned recovery package. First implementation supports an unprotected, same-codec, data-only edge consisting of one primary-key-preserving transform on one store. A multi-operation edge, changed key/index, encrypted policy, or unavailable original package requires application support outside this capability. Ordinary later revisions cannot run until the interrupted edge reaches a verified target.

The application supplies deterministic repair and verification functions and decides which package may execute. The engine owns the snapshot boundary, all cursors and transactions, readiness, lineage, and error reporting. Recovery does not undo old writes on its own.

## Package and journal

This illustrates the neutral document, with abbreviated digest values. A production package uses full exact-byte SHA-256 references and a reviewed immutable release. Target manifest and module origins follow the ordinary deployment rules.

```json
{
  "protocolVersion": 2,
  "requiredCapabilities": ["resumableRepairV1"],
  "recoveryId": "repair-0021-invoice-tax",
  "schemaId": "invoices",
  "source": {
    "committedRevision": 20,
    "committedSchemaHash": "sha256:…",
    "interruptedMigrationId": "0021_ApplyTax",
    "interruptedMigrationHash": "sha256:…",
    "operationIndex": 0
  },
  "target": { "revision": 21, "schemaHash": "sha256:…" },
  "originalManifest": { "path": "./release-0021/manifest.json", "sha256": "sha256:…" },
  "module": {
    "path": "./repair-invoice-tax.mjs",
    "sha256": "sha256:…",
    "repairProcessed": "repairProcessed",
    "repairUnprocessed": "repairUnprocessed",
    "verify": "verify"
  }
}
```

A deployment pins the recovery document itself by URL and exact digest. Before any mutation, the engine verifies the original manifest/schema/edge/module, normal history, recovery identity, target fingerprint and capabilities, and the interrupted operation. The package cannot supply a replacement checkpoint or override the original digest. Original bytes and normal history stay publishable and immutable.

The existing native checkpoint determines each row's partition. The inclusive prefix has already run the original transform. The suffix, strictly greater than the native key, has not committed it. An empty checkpoint means the prefix is empty. The engine uses native IndexedDB ordering, preserves original primary keys, and never publishes the checkpoint key. Both functions must produce records satisfying the **original target**; if old transformed data cannot be repaired from its persisted inputs, the application needs a different recovery strategy. Recovery V1 does not retarget a revision or represent a rollback.

| State | Transition and durable result |
| --- | --- |
| `migrating` at revision 20 | Under the ordinary per-database exclusive lock, verify the expected journal and native version in a transaction. Admit only an explicitly requested matching package. Bump native version and atomically record `recovering`, original checkpoint and immutable recovery digest. Cooperating clients close on version change. |
| `recovering` | Scan by primary key, using the original boundary to select the handler. Write transformed rows and the recovery cursor together. After crash or cancellation, the exact recovery package resumes after the recovery cursor. The original checkpoint and prior history remain intact. |
| `recovering`, all rows written | Run a checkpointed verification scan against the target contract and app verifier under the same readiness barrier. Stay blocked if verification fails. Final commit revalidates the starting history, recovery checkpoint and target layout before publishing readiness. |
| `ready` at revision 21 | Append the original edge and digest with a distinct recovery outcome and the recovery ID and digest. Future engines verify both against the deployment's immutable bindings. The old journal evidence is preserved privately with key-redacted public summaries. |
| Original edge already complete | Return `AlreadyCompletedOriginal`, write nothing and do not run repair handlers. This package cannot undo the completed edge. A later normal revision may normalize both successful outcomes if the application requires it. |
| Recovery already complete | The same ID and digest returns `AlreadyRecovered`. A different recovery package, different branch or unsupported topology is rejected before writes. |

Example history entry after recovery:

```json
{
  "id": "0021_ApplyTax",
  "sha256": "sha256:…",
  "outcome": "recovered",
  "recovery": { "id": "repair-0021-invoice-tax", "sha256": "sha256:…" }
}
```

A protocol-version bump for recovered journals and the native version change are required so earlier managed adapters fail readiness. A recovery-aware deployment learns the retained approved edge/recovery bindings through its static manifest. Ordinary opens never select a repair automatically. Raw writes by uncooperative same-origin code remain an application deployment boundary.

## Proposed application entry points

```js
const result = await engine.recoverDatabase({
  name, schema, manifestUrl,
  recovery: { url: recoveryUrl, sha256: pinnedDigest },
  signal, onEvent
});
```

```csharp
await using var events = await magic.ObserveMigrationsAsync(ObserveMetadata);
var db = await magic.Database(InvoiceDatabases.Local);
var result = await db.RecoverMigrationAsync(new(recoveryUrl, pinnedDigest), cancellationToken);
```

The C# method translates into the same explicit engine call. The CLI would validate the exact original graph and publish the reviewed package. The inspection/cancel surfaces remain scoped to the physical database. Proposed structured diagnostics include `MAGDB_RECOVERY_REQUIRED`, `MAGDB_RECOVERY_MISMATCH`, `MAGDB_RECOVERY_PACKAGE_REQUIRED` and `MAGDB_RECOVERY_VERIFICATION_FAILED`. Events carry phase, code, revision, recovery ID and counts. They omit records, keys and hook exception text. Cancellation, missing assets and quota report known operational retry conditions. A hook or verifier failure requires developer diagnosis before attempting again.

## Acceptance and maintainer decisions

Approve the initial single-transform restriction, the protocol bump/old-client rejection, and whether the app's reviewed verification function is a sufficient semantic acceptance rule beyond the engine's key and layout checks. Confirm that already-completed clients should take the ordinary forward path if outcome normalization is needed. Decide which immutable recovery bindings must be retained in future manifests and how release review authorizes the explicit app action. No new API or journal writer is implemented here.

Acceptance requires unit/CLI fixtures for strict neutral parsing and byte/history pins, plus actual Chromium, Firefox, Linux and macOS WebKit tests spanning:

- Resume after cancellation/crash before and after each recovery checkpoint and the final transition. No replay of committed recovery writes.
- Prefix and suffix partitioning for empty, final, sparse and compound native keys. Verify actual prior mutations and unprocessed records, unchanged keys, retained original bytes, and exactly one recorded disposition.
- Original failure, mismatched package and module/hash tampering, stale/competing tabs and workers, quota abort, verifier failure and incomplete/offline asset delivery. Preserve data and keep CRUD blocked until successful verification.
- An older managed adapter, clients that completed the original edge, repeated successful recovery, invalid schema/codec/protection or multiple operations, and ordinary later revisions from either legitimate completed outcome.
- Typed Blazor inspection, explicit invocation, cancellation and event disposal in trimmed and AOT builds. Metadata remains redacted even when a custom hook throws record data.
