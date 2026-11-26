# Managed data encryption key rotation

Status: implementation under validation.

## Existing boundaries

The browser engine owns IndexedDB transactions. AES-GCM envelope version 1 already
authenticates the key ID alongside the origin, physical database, account, store,
native primary key and protected member. Existing field and whole-record formats
can carry a new generation without rewriting unchanged databases. Reuse
`encryptionRecord.js` for the protected record representation and preserve native
keys, public indexes, public field values and lossless numeric wire values.

Managed vaults already authenticate their protection policy and migration journal.
Protected writers compare the authenticated policy and journal snapshots in the
write transaction. Reuse that boundary for rotation admission, while keeping
rotation progress separate from schema revisions. The C# provider keeps key
custody and resolves generation IDs. It must explicitly attest that both keys
can be reacquired after restart. The library verifies usable keys and
authentication locally. It cannot verify the provider's future availability.

## Admission and authorization

Rotate a ready authenticated managed vault, independently of its schema or native
IndexedDB version. An uninitialized vault, an unresolved migration or recovery,
a conflicting rotation, a reused generation ID, or a target key that cannot be
recovered under the provider contract fails before record mutation.

Admission atomically replaces the existing policy sentinel with an authenticated
rotation fence, publishes the target write generation and adds a rotation
journal. Compare the previous authenticated policy, migration proof, journal,
numeric metadata and layout before committing. An older cooperating connection
will reject the changed sentinel in its existing transactional guard. An older
reopening engine cannot interpret the rotation sentinel as an ordinary policy.
Uncooperative raw IndexedDB writers require an application-owned cutover.

Bind each rotation to the vault/account identity, original and target generations,
verified schema/model identity, immutable mode and operation identity. A dual-key
transition proof binds both keys to the same admission and progress state.
A generation ID is a routing hint until AES-GCM authenticates the envelope
against its key and storage context. Retain authenticated generation lineage so
a previously used ID cannot be bound to a different key. Detect modification,
mismatched proof and checkpoint replacement relative to the admitted session.
Restoring a complete internally consistent old storage snapshot cannot be
detected without a trusted freshness anchor outside local storage.

## Access and transaction invariants

| Mode | Reads | Mutations |
| --- | --- | --- |
| Exclusive | Fenced | Fenced |
| Read-only | Authorized dual-generation reads | Fenced |
| Online | Authorized dual-generation reads | Target-generation only |

The coordinator is scoped to a vault; Web Locks may serialize coordinators, but
the persisted state comparison in each transaction enforces correctness. All CRUD
entry points, including deletes, bulk writes and public patches, enforce the
same state guards. A write prepared before a generation or authorization change
must abort when its transaction observes that change. Reads and streams check
the applicable fence and authorization before releasing data. A stream may fail
during transitions and can be restarted; it must stop releasing newly acquired
data once its authorization is invalid.

Capture bounded native cursor batches and prepare cryptography outside the
write transaction. Compare the complete current stored snapshot and the exact
authenticated progress snapshot inside the transaction before writing. A
concurrent delete remains deleted. A concurrent target write survives. A public
patch requires retry from the current snapshot when it changes the record
contents used by the conversion. A checkpoint advances only with committed
record writes or with proof that a skipped record is now target generation or
absent. Keep reads and mutations on unrelated databases independent. Do not
expose keys, plaintext, or native checkpoint keys in diagnostics.

## Resume and retirement

Pause, interruption, cancellation or lock retains atomic checkpoints and the
selected availability mode. Resume authenticates persisted admission and
progress and reacquires required keys. Missing authorization, wrong keys and
integrity errors must leave the vault recoverable, without deleting or silently
recreating data. Lock revokes the browser session and its pending operations;
cancelling a .NET wait remains distinct from aborting browser work.

A finalization fence blocks writes long enough to establish a complete,
resumable authentication pass with the target key. Cursor completion alone
never permits retirement. Finalization commits the target-only active policy,
reseals managed authentication metadata and commits retirement evidence
atomically. Subsequent managed migrations must preserve authenticated generation
lineage. Reopening a completed vault requires only the target key. Logical
readiness for retirement applies to the active managed vault and supported
cooperating connections. It is separate from provider key destruction,
backup retention, physical storage erasure and other contexts' memory.

## Acceptance

Exercise each mode at every transaction boundary in real IndexedDB and Web
Crypto. Cover mixed generations, all write routes, concurrent coordinators,
stale snapshots, revocation, tampering, crash or page termination, target-only
reopen, genuine stable-package adoption, and later authenticated migration.
The hosted matrix must include Chromium, Firefox, Linux and macOS WebKit and
published trimmed/AOT Blazor. Benchmarks should measure conversion, verification
and foreground latency without setting timing-sensitive pass thresholds.
