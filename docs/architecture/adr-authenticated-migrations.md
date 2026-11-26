# Authenticated migrations and mutable protection policies

Status: implemented preview extension; native-browser, published-WASM, and
application rollout acceptance must be reviewed at the exact release commit.
This extends the ordinary migration/recovery implementation reviewed at `790d2f5`.
That earlier approval does not certify the authenticated extension.

## Goals and ownership

Magic's persisted model, static migration package, engine, and encryption policy
must describe one database lifecycle. C# is the first authoring experience. It is
not a runtime dependency of the migration protocol. A future TypeScript exporter
or a developer writing neutral JSON must obtain the same validation and execution.

The CLI compares persisted schemas, scaffolds supported operations, records
explicit decisions, seals published assets, and fails CI for unresolved or invalid
plans. The shared engine owns asset preflight, native layout checks, ownership,
readiness, transaction boundaries, progress, and recovery history. The protected
adapter owns authenticated record/policy encoding and a revocable authorization
lease. Existing AES-GCM envelopes, record codecs, and protected CRUD are reused.

Applications own key acquisition, account choice, user prompts, telemetry, and
deployment cutover. A PIN, WebAuthn flow, API, or operating-system credential is an
application/key-provider concern. No key, password, biometric, or derived secret
belongs in the static package or a migration checkpoint. Custom transformation
modules are trusted application code and receive logical decrypted records in
memory. They are not sandboxed plugins and must avoid external side effects.

## Implemented policy transitions

| Source | Target | Behavior |
| --- | --- | --- |
| Public values | Encrypted fields | Rewrite existing records under the target field policy |
| Encrypted fields | Public values | Require explicit protection-loss approval, decrypt and rewrite |
| Field encryption | Whole-record payload | Re-encode under the target public allowlist |
| Whole-record payload | Field encryption | Require explicit approval; unknown members were previously protected |
| Whole-record allowlist | Smaller allowlist | Encrypt formerly public members |
| Whole-record allowlist | Larger allowlist | Require explicit approval for newly public members |
| Protected database | Additional public index/store | Separate authenticated structural revision |
| Existing protected layout | Managed journal | Authenticate a reviewed baseline before adoption |
| Existing plaintext layout | Authenticated history at the same physical name | Explicit physical binding, baseline, and initialization |

Changing only protection does not require handwritten crypto or a custom
JavaScript transform. The built-in operation is:

```json
{"op":"reprotect","store":"supportLogs"}
```

A declassification decision belongs to the affected store operation:

```json
{"op":"reprotect","store":"supportLogs","allowProtectionLoss":true}
```

The same decision can accompany a custom `transform` or another supported data
operation that performs the complete rewrite. Removing a C# attribute does not
silently approve exposure. The CLI leaves an unresolved decision until the author
records approval. Validation also checks the decision at runtime. Dropping a store
is still a separate explicit `allowDataLoss` decision.

Public/field policies are finite sets; whole-record protection is a default-deny
allowlist. Consequently, moving from whole-record to finite field protection is
a protection-loss operation even if today's declared fields appear equivalent:
unrecognized persisted members could otherwise become public.

## Stable identities and enrollment

Logical schema ID, physical database name, account scope, model revision, package
version, and protocol version have separate meanings. A policy change does not
rename the database, select another account, or reset its history.

An authenticated database remains authenticated after its last encrypted field
becomes public. It retains its account binding, policy sentinel, journal proof,
and protected session route. Ordinary access still requires authorization. This
permits later encryption without inventing another database or losing lineage.

Existing plaintext databases can be enrolled explicitly using
`MagicEncryptionOptions.PhysicalDatabaseName`. The application's internal route
remains reserved and account-bound; only the selected physical storage is reused.
The option requires a pinned migration manifest. The manifest must contain a
baseline for an unmanaged database, or the matching retained managed history.
`InitializeAsync` explicitly establishes the new authenticated identity. An
existing authenticated database uses `EnsureUnlockedAsync` and its original key.

This is in-place enrollment. It does not copy between namespaces or merge data
from different accounts. The application must choose and validate the binding.
Previously shipped plaintext writers still need an application-owned cutover;
the new sentinel cannot make old code that never checked encryption metadata
cooperate. Existing protected writers do check the policy sentinel and are
invalidated when adoption or migration replaces that exact envelope.

The presence of `__magic_encryption` is an authenticated-storage boundary even if
its policy value is missing. Deleting a policy must not turn a vault into an
ordinary plaintext database. Initialization cannot replace a damaged authenticated
identity with a new key. Missing or invalid metadata fails closed.

A managed policy sentinel is not a legacy adoption credential. If its journal or
proof is deleted or downgraded, adoption fails instead of erasing authenticated
history and replaying transforms. Only an authenticated legacy-format sentinel
may establish the first managed journal for an existing protected database.
An existing v3 journal requires a managed sentinel. A matching, authenticated
legacy envelope remains valid only for first adoption. During preflight and
execution, the runner pins the observed journal and policy/proof bytes to detect
replacement with an earlier valid journal or checkpoint.

## Portable capability and journal versions

Schema and migration documents remain protocol v1. Authenticated histories declare
`authenticatedMigrationsV1` in required capabilities. The CLI retains that
capability after declassification, including all-public subsequent revisions.
Published capabilities are sealed alongside assets and reference bindings.

Recovery bindings continue to use manifest v2 and `resumableRepairV1`. A protected
recovery manifest requires both capabilities. Existing ordinary journals remain
v1; ordinary recovered journals remain v2. An authenticated journal is v3 from
bootstrap or adoption onward, including completed and interrupted recovery.
Older engines reject this format instead of interpreting authenticated progress
as ordinary progress. Authentication is required even when every target field is
public.

The public policy sentinel remains at `__magic_encryption/policy`. Managed policy
fingerprints have a distinct, canonicalized native-layout/protection/numeric
contract. Legacy sentinel bytes are authenticated with the existing envelope
format, then their complete contract is normalized and compared with the nominated
baseline. Ordinary legacy CRUD fingerprints remain byte-for-byte unchanged.

The released protected Lossless format authenticated its numeric contract in the
legacy sentinel. A reviewed first adoption may install the separate managed
numeric-policy store if that store was never present. An existing numeric store
with a missing policy, or a missing managed policy after adoption, fails closed.

The complete journal is authenticated by a separate encrypted proof under
`__magic_encryption/migration-state`. Its encoding preserves native key types,
including Date and binary checkpoint identities. The proof binds revision,
schema hash, status, operation position, cursor, counts, migration digest, recovery
identity, and retained history. Typed encoding prevents JSON coercion from making
different native cursor values appear equivalent.

This supplies local integrity and identity binding. It does not provide an
external monotonic counter capable of detecting restoration of an entire old,
internally consistent browser-storage snapshot. Applications needing that
property must supply an external authority.

## Authorization and readiness

Protected registration is lazy and copies its semantic inputs. It registers the
target model and manifest without creating a database, running the app, requesting
a key, or changing policy. Existing C# `InitializeAsync` and `EnsureUnlockedAsync`
obtain provider authorization, then invoke managed initialization. The long-running
migration runs outside the unlock semaphore so `LockAsync` can revoke it promptly.

The direct JavaScript wrapper separates authorization and execution:

```javascript
await api.unlockProtectedDatabase(route, keyId, authorizedKey, initialize);
await api.ensureProtectedMigration(route);
```

The portable engine accepts a branded in-memory adapter from
`createProtectedMigrationAdapter`. It is bound to the exact physical name,
account, key identity, and revocation signal. Another language can use this same
boundary without Blazor. Ordinary registration cannot supply an arbitrary object
to bypass validation.

Missing authorization is a recoverable lifecycle condition. Notifications use
`authorizationRequired`, `authorizationRejected`, and `authorizationLost`, with
stable diagnostic codes. Existing `preflight`, `migrating`, `checkpoint`,
`recovering`, `recoveryCheckpoint`, `ready`, and failure events remain available.
No separate UI or automatic prompt is introduced. Observer exceptions cannot
change transaction outcomes, and events contain no record contents or keys.

Successful readiness pins the ready journal and proof. Protected CRUD compares
the expected policy, numeric policy, and journal/proof bytes. Protected writes
include these metadata stores in their native transaction. Managed access remains unavailable
while a migration or recovery is incomplete.
Public patches on managed storage also pin the session lease through the write
boundary, including when the target policy makes every record member public.

## Execution and crash consistency

Before mutation, the engine verifies required immutable assets, the source model
and layout, the source policy, and any existing journal proof. Authorization is
checked around asynchronous work and again at the write boundary.

Admission replaces the ordinary ready sentinel with an authenticated migration
fence in the same native upgrade that records the active journal. The fence binds
the schema ID, source revision/hash, and original migration identity/digest. An old
protected session's exact-envelope check now fails. A new ordinary unlock cannot
mistake the fence for a ready policy.

A data batch proceeds as follows:

1. Read the next native-key range using the committed cursor.
2. Decrypt source records under the source policy.
3. Run the supported logical transform and validate stable primary keys.
4. Encode records under the target protection policy and prepare the next journal
   proof. All Web Crypto work finishes before opening the write transaction.
5. In one transaction, recheck the full prior journal, exact policy/proof envelopes,
   native layout, numeric policy, and authorization. Commit records, cursor/count,
   and new proof together.

Protected record values are never persisted in temporary plaintext stores,
diagnostic causes, or recovery evidence. Public target values are written only
under an approved target policy. An interrupted protection increase naturally
leaves untouched records in their original representation until resumed; the
database is not reported ready during that interval.

Current authenticated data revisions permit one logical data operation per store.
Several logical edits on one store belong in one custom transform. This keeps
source/target representation unambiguous for every cursor partition. Structural
changes use adjacent atomic revisions. Adding a public index does not decrypt and
rewrite every record. Authentication still covers its new layout and policy.

Finalization atomically installs the target ready policy, completed history,
revision/hash, and authenticated ready journal. A cancelled or terminated client
resumes with fresh authorization and the same immutable package. Committed work
is not rolled back. The engine does not delete the database, reset history, force
completion, or automatically choose an alternative recovery.

These boundaries follow the transaction lifecycle in the
[IndexedDB specification](https://www.w3.org/TR/IndexedDB-3/#transaction-lifecycle)
and the asynchronous operations in the
[Web Cryptography specification](https://www.w3.org/TR/webcrypto/).

## Authenticated emergency recovery

The existing approved-recovery protocol supports one interrupted custom transform
with stable keys and record codecs. The original processed partition is decoded
under the original target policy; untouched records use the original source
policy. Repaired records use the target policy. Verification reads all repaired
records under that target policy and requires an explicit true result.

Repair and verification have separate durable cursors. Every checkpoint/proof
commits with its record writes. Recovery history records the original edge and
approved recovery identity/digest. Private recovery evidence is encrypted for an
authenticated database. Subsequent ordinary revisions retain journal v3 and the
recovered lineage.

C# `RecoverMigrationAsync` requests authorization directly. It does not first
rerun the original failing migration. Recovery completes only its approved edge;
ordinary `OpenAsync` advances later revisions. Recovery of a failed recovery,
arbitrary multi-operation repair, and irrecoverable source data remain separate
design problems. No repair package can recover information that has already been
lost.

## Deliberate boundaries and future extension

The implemented adapter keeps one data-encryption key throughout a transition.
Data-key rotation is explicitly rejected with `MAGDB_KEY_ROTATION_UNSUPPORTED`.
Changing a PIN or rewrapping that same data key remains a provider operation and
does not inherently require rewriting database records.

A future rotation capability must introduce two independently authorized key
roles, bind their identities and source/target policies into immutable assets and
the authenticated fence, define mixed-key partition recovery, and commit final
policy/proof under the target key. It must expose when the old key is no longer
needed by this database without deleting or revoking application-owned keys.
Locking either required lease must halt subsequent writes. This requires a
reviewed provider/API contract and its own real-browser/old-client tests; accepting
a targetKey argument without those guarantees is not an implementation.

Protected primary keys and native index members remain public and use the
existing supported storage-context rules. Resumable primary-key changes,
authenticated store renames (ciphertext AAD includes the store), structural
staging/swap, cross-database moves, and numeric-format conversion are rejected.
Protection and logical record-codec changes are distinct decisions. Future
capabilities must preserve immutable history and have explicit negotiation rather
than changing the interpretation of previously published assets.

## Acceptance

See the [implementation handoff](../engineering/protected-migrations-handoff.md)
for the exact real-environment acceptance work. Source code and test registration
are not execution evidence. Validate the same final commit through the existing
browser matrix, installed CLI, and published trimmed/AOT applications, inspect raw
storage at intermediate checkpoints, and retain the actual logs and artifacts.
