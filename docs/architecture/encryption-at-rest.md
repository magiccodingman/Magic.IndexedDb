# Encryption at rest

This design covers application-level encryption of IndexedDB data. The browser
protection path and opt-in C# API are implemented. The v3 preview connects them to
[authenticated managed migrations](adr-authenticated-migrations.md), including
changes in both protection directions and explicit in-place enrollment. Actual
data-key rotation and protected LINQ evaluation remain future integration phases.
The prototype measurements below describe the earlier implementation; use the
[encrypted migration guide](../guides/encrypted-migrations.md) for the current
deployment lifecycle and its acceptance requirements.
The browser prototype also includes an authenticated policy sentinel and codecs for field and record protection. The browser storage module routes configured protected stores through these codecs for ordinary CRUD and query calls. The C# API configures account-bound databases, maps annotated fields, and exposes unlock, public projection and public patch operations.

## Existing paths

The C# serializer maps properties to persisted names and sends typed requests to
JavaScript. The storage module implements single and bulk writes, direct key and
whole-table reads, and shared LINQ index/cursor branches. The v3 shared migration
engine owns managed schema deployment and readiness. Existing protected
registration without a manifest retains its new-vault bootstrap behavior.

Enforce encryption in the browser storage boundary using the effective persisted names. A C# converter alone would miss browser-side writes and would prevent the JavaScript planner from distinguishing a protected predicate from a predicate it can evaluate. Async Web Crypto calls should complete outside live IndexedDB write transactions. Prepare a batch before opening its transaction so an encryption failure cannot partially persist it.

## Public API

- `[MagicEncrypt]` on a mapped property selects field encryption. Invalid combinations with ignored properties, keys or indexes fail schema validation.
- A database policy selects explicit field protection or record protection. Record protection encrypts every serialized member except the explicit allowlist of exposed members. Applications storing sensitive records should use this policy because new members are protected automatically.
- The existing `AddMagicBlazorDB` registration remains valid. Add a separate encryption registration that names protected databases and registers an asynchronous application key provider.
- `IMagicEncryptionContexts` activates an account-bound view of `IMagicIndexedDb` and its encryption lifecycle API. A provider request identifies the database, account scope and requested key ID. Providers can supply a raw 32-byte key or a browser-local opaque AES-GCM key reference. The app explicitly requests an unlock. Ordinary queries never call the provider.
- Use a distinct physical browser database per protected logical database and account. Derive the physical namespace from stable, non-secret application identifiers. Existing query handles retain their account binding and are invalidated by account switch. Browser key ownership is scoped to the app instance. A reload or new tab starts locked.
- Hold one in-flight unlock per scope. Lock increments a generation before clearing keys, cancels the provider where possible and prevents stale completion from installing a key. Recheck the generation after asynchronous crypto before yielding or scheduling a write. Writes already committed and results already materialized remain available to their consumers.

Authentication, authorization, WebAuthn, MFA, device enrollment, session expiration, PIN policy, key recovery and regulatory compliance belong to the application. A consumer can use WebAuthn PRF or another mechanism to unwrap a random data key and hand that key to Magic.

## Developer experience

Register Magic and the provider before the app knows the account. Inject
`IMagicEncryptionContexts` and activate the protected context after the app
authenticates the account and determines its organization or tenant.

```csharp
services.AddMagicBlazorDB(BlazorInteropMode.WASM, isDebug: false);
services.AddMagicIndexedDbEncryption<AppKeyProvider>();
```

```csharp
var active = await contexts.ActivateAsync(
    [new(database.DatabaseName, organizationAndAccountScope, "notes-key-v1")]);

// For an existing vault. Explicitly provision a new empty vault with
// active.Encryption.InitializeAsync(database) instead.
if (await active.Encryption.EnsureUnlockedAsync(database, cancellationToken))
{
    var query = await active.Database.Query<CaseNote>();
    var notes = await query.ToListAsync();
}
```

`AppKeyProvider` implements `IMagicEncryptionKeyProvider`. Return
`MagicEncryptionKey.FromBytes(dataKey)`, `MagicEncryptionKey.FromBrowserKey(reference)`,
or null when no key is available. The app owns credential interaction,
recovery and key custody.

Use `[MagicEncrypt]` on individual properties or
`[MagicEncryptRecord(nameof(Id), nameof(Title))]` to require whole-record
protection with explicitly public members. The allowlist includes every primary
key and native index. A configuration-based `RecordPolicies` allowlist uses
persisted names. If both record allowlists are supplied, they must agree.

Use `ActivateAsync` again to switch accounts or `DeactivateAsync` on sign-out.
Switching invalidates old protected query handles within the same DI scope.
The registration overload that supplies policies at DI resolution is available
for apps whose account is already known and fixed for that scope.

After this same connection has authenticated the policy successfully, it can
lock and still use `SelectPublicAsync(record => record.Title)` or
`PatchPublicAsync(new CaseNote { Id = id, Title = title }, record => record.Title)`.
The persisted policy must still match. A reload, another tab, or a fresh context
requires its own policy authentication before public operations.

## Query and write behavior

The table below describes the existing protected session without a managed
manifest. A managed authenticated context additionally requires completed
migration readiness and active authorization; locking invalidates its readiness.
Reauthorize before using the managed context again, including an all-public
target model.

| Operation | Fresh connection without policy authentication | Locked after authenticating this connection | Unlocked with matching policy |
| --- | --- | --- | --- |
| Full entity read | Reject | `MagicEncryptionLockedException`, including for empty results | Decrypt automatically |
| Explicit public projection | Reject | Allow public predicates and ordering while the policy matches | Same query plan |
| Public partial update | Reject | Allow public members while the policy matches and the primary key stays unchanged | Preserve ciphertext byte for byte |
| Full write or delete | Reject | `MagicEncryptionLockedException` | Encrypt protected data before storage, or delete the record |
| Protected predicates and sorts | Reject | `MagicEncryptionLockedException` on full queries | Reject explicitly until hybrid LINQ is implemented |

Projection needs an explicit persisted-member selection so the storage boundary can avoid materializing protected fields. A typed projection must never hand back a partially populated entity that a caller could write over the real value. Map the same property paths as the expression translator, including nested paths descending through a protected property. Keep existing index and cursor behavior for public predicates. Raw ciphertext must never reach the existing expression evaluator as a substitute for the logical value.

For hybrid execution, use public indexed predicates as a candidate filter only when their candidate set is a guaranteed superset of matches. For OR branches, independently produce candidates and union by primary key before residual evaluation. Decrypt after leaving the native candidate transaction or by using bounded batches. Evaluate residual predicates against plaintext before counts and pagination. Apply global ordering and pagination once after union and residual evaluation. Streaming can remain bounded for scan-order queries. Global protected ordering may require materialization. Avoid logging any decrypted row or protected predicate operand.

## Storage and threat model

The browser primitive uses Web Crypto AES-GCM with a 256-bit key, random 96-bit nonces, and a full 128-bit authentication tag. A versioned envelope stores the key identifier, nonce and ciphertext. Associated data binds its version and key ID to the origin, physical database, account, store, actual primary key and protected member or record payload. Primary keys need a canonical representation and must be known before encryption. Start with explicit stable keys and reject auto-increment on protected stores until key allocation can satisfy this requirement. Fresh random nonces give probabilistic uniqueness. Before production, define a per-key invocation budget below the applicable AES-GCM limit across every tab and device that uses that key, and a rotation policy that keeps deployments within that budget.

A protected database should have durable, versioned policy metadata and an authenticated key check. Initialize metadata atomically across tabs. Existing physical databases with missing or mismatched metadata require an explicit migration decision. Policy changes must not silently downgrade protection. Namespace separation keeps older Magic clients writing to the original database during rollout. The application must retire those clients and clear or otherwise handle old data after cutover.

The intent is to protect stored contents when an attacker can inspect or alter browser storage without access to the unlocked key. Envelope authentication detects ciphertext modifications and substitution across authenticated contexts. It cannot detect replay of a previously valid value in the same context. Public indexes, keys, row counts, value lengths, access patterns and storage metadata can remain visible. Same-origin code running while unlocked can access useful data. This design does not provide HIPAA compliance by itself.

## Phases and compatibility

1. Establish the cryptographic format and real-browser tests. Measure encryption and decryption separately from storage and interop.
2. Add scoped key acquisition, versioned policy metadata and locked-state enforcement. Add field and record policy wiring for new databases, and cover every write and read entry point. Expose public projections and narrow patches before allowing locked workflows. Reject unsupported migration, schema and query combinations explicitly.
3. The v3 preview implements explicit same-key migrations, authenticated
   checkpoints, tab fencing and approved protection loss. An explicit physical
   binding permits reviewed in-place enrollment; cross-namespace copies remain
   application-owned. Actual key rotation still requires source/target key roles,
   mixed-key recovery and a retirement contract. Old browser copies, backups and
   storage remnants remain application concerns.
4. Add the hybrid planner path. Verify semantics against a plaintext reference dataset with compound keys, mixed AND and OR predicates, residual filtering, sorting ties, pagination, count, cancellation and streaming.

The integration must preserve existing unencrypted models and database names. Before enabling PHI workloads, exercise a migration and recovery drill and review the application's plaintext surfaces and key custody.

## Browser acceptance matrix

Run the existing unit and browser suites. Add direct IndexedDB inspection for all single and bulk writes, retry and failed-batch tests, AAD tampering and cross-record swaps, wrong keys and unsupported envelopes, stale unlock completion, reload, simultaneous tabs, cancellation, account and database separation, policy changes and restart during migration. Add locked public projection and patch tests that compare the original ciphertext byte for byte. Compare native-index and residual query outputs against an equivalent plaintext dataset. Use Chromium, Firefox and WebKit, and include a browser-hosted WASM app rather than relying on server interop alone.

Report browser, hardware, dataset size and field sizes, run counts, warmup and percentile or median. Compare plaintext and protected single writes, bulk writes, indexed reads and scans. Report key acquisition separately from steady-state crypto and inspect persistent bytes independently of typed query results.


## Prototype measurements

The first milestone includes a reproducible native IndexedDB benchmark. Run `dotnet build E2eTest -c Release` and then `node E2eTest/benchmarks/encryption-envelope.mjs`. It compares plaintext with one protected JSON value per row and keeps the public key and index identical. It measures Web Crypto plus native storage, excluding app key acquisition, C# interop and LINQ planning.

On desktop Chromium 151 on Linux with an AMD Ryzen 9 7950X3D, the medians over ten measured pairs were as follows. Each round used a fresh database, with one warm-up pair and alternating order.

| Operation | Plaintext | Encrypted |
| --- | ---: | ---: |
| 100 sequential point writes | 14.50 ms | 17.05 ms |
| 1,000 row batch write | 23.75 ms | 35.05 ms |
| 100 sequential point reads | 6.80 ms | 9.90 ms |
| Public index read of 100 rows | 0.90 ms | 2.10 ms |
| Full read of 1,000 rows | 5.70 ms | 14.75 ms |

These timings use 256-character values, a preloaded key and binary envelopes. Browser scheduling, field size and device performance can change the results. Remeasure with the integrated typed API before making application capacity estimates.

## References

- [Web Cryptography Level 2](https://www.w3.org/TR/webcrypto/)
- [Indexed Database API 3.0](https://www.w3.org/TR/IndexedDB/)
- [NIST SP 800-38D](https://csrc.nist.gov/pubs/sp/800/38/d/final)
- [HHS Security Rule summary](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)

## Published typed WebAssembly benchmark

Run a Release browser test build and publish the example WASM app. Then run
`node E2eTest/benchmarks/encryption-wasm.mjs <published-wwwroot>`. This benchmark
uses the same typed model and public index in both modes, and invokes ordinary
Magic APIs. Its fixture key is public and exists solely to test the integration.

The desktop Chromium 151 medians below are from ten fresh-context pairs after
one warm-up pair on Linux with an AMD Ryzen 9 7950X3D. Each table had 1,000 rows
with one 256-character value. The run also verified that a full page reload
starts locked and that explicit re-unlock recovers the persisted data. A separate
lifecycle run verified same-tab SPA navigation retains the key and a new tab
starts locked.

| Operation | Plain | Protected |
| --- | ---: | ---: |
| 25 sequential typed writes | 64.60 ms | 65.95 ms |
| 1,000-row typed batch write | 136.25 ms | 144.35 ms |
| 25 sequential typed point reads | 139.05 ms | 138.50 ms |
| Typed public index read of 100 rows | 29.05 ms | 33.25 ms |
| Typed full read of 1,000 rows | 151.95 ms | 145.55 ms |

Provisioning with the already available synthetic key took a median of 4.55 ms
after database setup. These are desktop observations. The negative differences
for point reads and scans reflect run-to-run variability. They do not imply
that encryption speeds up those operations. User interaction, authentication,
remote key access, and browser page load are outside these measurements.

This earlier 1,000-row run used interpreted Release WASM. The [larger hardening measurements](#larger-published-wasm-and-aot-measurements) include both interpreted WASM and AOT. Protected data is isolated in a new physical
namespace. That benchmark predates authenticated migrations. Without an explicit
managed physical binding, adding encryption still selects a separate protected
namespace and leaves plaintext sources in place. The manual copy below remains
an application-owned alternative for cross-namespace transfers.

### Manual cutover for existing data

1. Export the old plaintext source before enabling protection for its logical name.
   Keep the export under the application's existing access and retention controls.
   Adding `[MagicEncrypt]` makes the typed model refuse an unprotected query.
2. Stop writers to the old namespace, including older clients and tabs. Provision
   the empty protected target explicitly through `InitializeAsync`. Preserve stable
   record keys and use a fresh protected namespace for incompatible policies.
3. Read source rows outside IndexedDB write transactions and import them through
   the protected Magic API. Use bounded batches and verify logical contents,
   counts, and raw on-disk envelopes. Never put plaintext directly into the target.
4. Keep external checkpoints keyed to the source and target if the copy must
   resume. Check which primary keys committed before retrying. A failed protected
   bulk write rolls back that transaction. It does not roll back previously
   committed batches.
5. Switch clients after verification. Enforce the application's rules for the
   original database, exports, backups, and recovery keys. No automatic cleanup,
   downgrade, cross-tab copy lock, or live key rotation is provided by this manual
   copy procedure. The managed same-database path has its own authenticated
   ownership, checkpoint and recovery contract.

## Hardening review

### Trusting the persisted policy

A connection must authenticate its policy sentinel with the data key before
it can access any store in the protected physical database. An earlier build
accepted locked public operations based solely on the current configuration.
The browser regression reproduces a second connection treating a previously
encrypted member as public and writing plaintext over its envelope.

A connection now retains the exact authenticated sentinel envelope in memory.
Reads require a matching envelope. Public projections check again before
returning their materialized results. Mutations check the sentinel inside the
same IndexedDB transaction as the mutation, including explicitly public
patches. Stores without member protection in a protected database still pass
through the policy gate. Native store and index creation runs only for a new
database. Opening an existing database cannot add indexes based on a stale
policy.

After an authenticated connection locks, explicitly public reads and patches
remain available while the exact sentinel is present. A reload, another tab,
or a newly activated context requires initial verification even for public
operations. Missing or changed metadata fails closed. If verification of a
stored envelope fails, the library cannot distinguish a wrong key from
tampering with that envelope.

### Runtime accounts and vault lifecycle

Register Magic and its provider before authentication. Inject
`IMagicEncryptionContexts` alongside the ordinary plaintext service.

```csharp
services.AddMagicBlazorDB(BlazorInteropMode.WASM, isDebug: false);
services.AddMagicIndexedDbEncryption<AppKeyProvider>();

// After the application resolves a verified account and organization.
var active = await contexts.ActivateAsync(
    [new("CaseNotes", stableOrganizationAndAccountScope, "notes-key-v1")]);
var state = await active.Encryption.GetStateAsync(notesDatabase);
var result = await active.Encryption.UnlockDetailedAsync(notesDatabase);
var notes = await active.Database.Query<CaseNote>();

// Sign-out or account change.
await contexts.DeactivateAsync();
```

Activation replaces the previous protected context. Its old query handles
are invalidated and its browser routes and keys are released. If browser
release cannot be confirmed, activation fails and the previous release can be
retried rather than activating another account over it. The plaintext
service is independent. One activation can hold multiple configured vaults.
The application supplies stable, collision-free account identifiers that
incorporate organization or tenant boundaries where required.

To intentionally discard a vault, call `await contexts.DestroyAsync(notesDatabase)`.
This invalidates the old context and deletes only the specified physical vault.
Activate again and call `InitializeAsync` to reprovision it. The app decides
whether it may discard unsynchronized work. A blocked native deletion can
wait for another tab or external connection to close. Other Magic connections
invalidate their protected routes on a browser version change.

Use `[MagicEncryptRecord(nameof(Id), nameof(PublicCategory))]` to make record
protection mandatory for a model. The attribute uses CLR property names and
resolves mappings. Configured allowlists use persisted names. If both are
provided, they must agree. Include every primary key and native index in the
allowlist. New members of annotated records are encrypted by default.

`UnlockDetailedAsync` distinguishes unlocked, already unlocked, interaction
required, cancelled, unavailable key, invalid key, uninitialized vault and
policy mismatch. The application can refresh state with `GetStateAsync` after
its own lifecycle events. There is no push notification API in this revision.

### Lock and concurrency guarantees

A lock cancels the provider request where possible and invalidates pending
unlock attempts. Ordinary unlock and explicit initialization are distinct
requests. The browser stops new protected operations and checks the session
before committing a prepared write or advancing a protected stream.
Already-committed writes and plaintext already returned to application code
cannot be recalled. A caller's cancellation token cancels only its wait, even if it was the only
caller. The provider can finish later and unlock the vault. The convenience
API reports caller cancellation by throwing `OperationCanceledException`.
`UnlockDetailedAsync` returns `MagicUnlockResult.Cancelled` for that wait.
On context invalidation the managed query path also drops queued results from
the old context.

If an application action means **cancel and stay locked**, explicitly await
`LockAsync`. The lock invalidates the pending attempt even when the provider
ignores cancellation and supplies a key later.

```csharp
// Capture the token when starting the action and retain its source in the UI.
using var unlockWait = new CancellationTokenSource();
var pending = active.Encryption.UnlockDetailedAsync(
    database, cancellationToken: unlockWait.Token);

// In the application's "Cancel and stay locked" action:
unlockWait.Cancel();
await active.Encryption.LockAsync(database);
```

The library wipes its temporary managed raw-key interop copy. The provider
owns its original bytes and any opaque JS reference it returns. The library
imports raw bytes into a non-extractable browser key, and releases its
reference on lock. The app still owns copies it keeps, provider-issued
references, application caches, cross-tab sign-out, and browser/OS memory.
General query trace diagnostics should contain structural metadata. Runtime
console messages omit row values and predicate argument objects.

### Isolated hybrid LINQ follow-up

1. Add planner metadata that classifies leaves and order keys by whether
   they require decryption. For each OR branch, derive only necessary public
   indexed conditions. A branch without a useful public condition needs a
   scan. Candidate filters must never exclude a valid match.
2. Acquire candidate primary keys or ciphertext in bounded cursor batches.
   Deduplicate by the canonical IndexedDB key. Outside the native read
   transaction, decrypt under one account and policy lease and evaluate the
   original expression tree. Reject unsupported comparators explicitly.
3. Apply projection, counts, ordering, stable primary-key tie breaks and
   pagination after residual evaluation. Never apply a candidate-level
   offset or limit that can change final results. Expose streaming where
   semantic ordering permits it, and document the materialization required
   for a full protected sort.
4. Test parity against the unencrypted planner for compound indexed
   conditions, AND/OR overlaps, null and missing properties, ordering ties,
   counts, pagination, cancellation and locking during a stream. Benchmark
   candidate reduction and the full-scan fallback separately. Keep the
   unencrypted planner path untouched.

### Larger published WASM and AOT measurements

Desktop Chromium 151 on Linux and a Ryzen 9 7950X3D. The same workload
uses 3,000 rows and a 2,048-character value. Bulk encryption schedules up to 32
records at a time, then persists the prepared batch in one transaction.
The runner alternates fresh contexts, excludes one warm-up pair, and
checks navigation, reload and a separate tab. Interpreted WASM used five
measured pairs and AOT used three. Medians are in milliseconds.

| Operation | Interpreted plain | Interpreted protected | AOT plain | AOT protected |
| --- | ---: | ---: | ---: | ---: |
| 25 sequential writes | 71.9 | 81.0 | 40.2 | 45.5 |
| 3,000-row batch write | 564.9 | 666.0 | 381.2 | 504.1 |
| 25 sequential point reads | 135.9 | 141.5 | 64.0 | 69.4 |
| Indexed read of 300 rows | 121.2 | 117.5 | 50.0 | 51.8 |
| Full read of 3,000 rows | 745.6 | 720.4 | 344.3 | 352.7 |

Use `dotnet publish TestWasm -c Release -p:RunAOTCompilation=true` for
the AOT application, then pass `chromium 4 3000 2048` after the published
`wwwroot` argument to the benchmark runner. The AOT run used the hardening
build before the final context-revocation follow-up, which does not change the
workload's CRUD route. The benchmark's first AOT attempt discovered two test
models named `Person`. The sample now registers its intended tables explicitly.
These end-to-end runs include browser transport, C# serialization and storage
rather than isolating cryptographic cost. Lower protected interpreted read
medians are observations from these runs.

Local verification passed 69 .NET unit and contract tests and 156 browser
tests per browser engine on Chromium, Firefox, and Linux WebKit. Focused
security cases live in `OpenTestEncryption.cs`; cancellation and key ownership
unit cases live in `EncryptionUnlockTests.cs`.
