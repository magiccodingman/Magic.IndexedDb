# Encryption at rest

This design covers application-level encryption of IndexedDB data. The draft implements the browser protection path and opt-in C# API. Automatic migration, key rotation, and protected LINQ evaluation are future integration phases.
The browser prototype also includes an authenticated policy sentinel and codecs for field and record protection. The browser storage module routes configured protected stores through these codecs for ordinary CRUD and query calls. The C# API configures account-bound databases, maps annotated fields, and exposes unlock, public projection and public patch operations.

## Existing paths

The factory creates Dexie stores from `StoreSchema`. The C# serializer maps properties to persisted names and sends typed requests to JavaScript. The JavaScript storage module has separate single add, put and update paths and separate bulk equivalents. Reads include direct key lookup, whole-table reads, and the shared LINQ planner with native index and cursor branches. The current migration module is a stub. The typed query interfaces return full entities and have no server-side projection operator.

Enforce encryption in the browser storage boundary using the effective persisted names. A C# converter alone would miss browser-side writes and would prevent the JavaScript planner from distinguishing a protected predicate from a predicate it can evaluate. Async Web Crypto calls should complete outside live IndexedDB write transactions. Prepare a batch before opening its transaction so an encryption failure cannot partially persist it.

## Proposed public API

- `[MagicEncrypt]` on a mapped property selects field encryption. Invalid combinations with ignored properties, keys or indexes fail schema validation.
- A database policy selects explicit field protection or record protection. Record protection encrypts every serialized member except the explicit allowlist of exposed members. Applications storing sensitive records should use this policy because new members are protected automatically.
- The existing `AddMagicBlazorDB` registration remains valid. Add a separate encryption registration that names protected databases and registers an asynchronous application key provider.
- A separate scoped `IMagicIndexedDbEncryption` service can expose `EnsureUnlockedAsync(database, cancellationToken)`, `LockAsync(database)` and an observable lock state without changing existing implementations of `IMagicIndexedDb`. A provider request identifies the database, account scope and requested key ID. A provider result supplies a raw 32-byte key or a browser-local opaque AES-GCM key reference, or reports cancellation or interaction required. A protected query may use an explicitly configured opt-in unlock policy. The default requires an explicit unlock call.
- Use a distinct physical browser database per protected logical database and account. Derive the physical namespace from stable, non-secret application identifiers. Existing query handles retain their account binding and are invalidated by account switch. Browser key ownership is scoped to the app instance. A reload or new tab starts locked.
- Hold one in-flight unlock per scope. Lock increments a generation before clearing keys, cancels the provider where possible and prevents stale completion from installing a key. Recheck the generation after asynchronous crypto before yielding or scheduling a write. Writes already committed and results already materialized remain available to their consumers.

Authentication, authorization, WebAuthn, MFA, device enrollment, session expiration, PIN policy, key recovery and regulatory compliance belong to the application. A consumer can use WebAuthn PRF or another mechanism to unwrap a random data key and hand that key to Magic.

## Developer experience

Register Magic as usual, then configure protected database policies and an asynchronous key provider:

```csharp
services.AddMagicBlazorDB(BlazorInteropMode.WASM, isDebug: false);
services.AddMagicIndexedDbEncryption<AppKeyProvider>(services =>
[
    new MagicEncryptionOptions("CaseNotes", GetAccountScope(services), "notes-key-v1")
]);
```

`AppKeyProvider` implements `IMagicEncryptionKeyProvider`. Return `MagicEncryptionKey.FromBytes(dataKey)` for a 32-byte data key or `MagicEncryptionKey.FromBrowserKey(reference)` for a browser-local AES-GCM key. Return null when no key is available. The app can unwrap its wrapped key after passkey or server authentication. It owns credential interaction, recovery and key custody.

Annotate a property on an existing Magic table with `[MagicEncrypt]`. Annotated models require a protected database configuration. For secure-by-default record protection, supply a `RecordPolicies` entry for the persisted table name. Its allowlist contains persisted property names, including the primary key and each native index. All other properties are encrypted, including ones added later. Using the attribute on sensitive members also guards against accidental omission of encryption registration.

Inject `IMagicIndexedDbEncryption`. Call `InitializeAsync(database)` only during explicit provisioning of an empty protected database. Normal page initialization can call `EnsureUnlockedAsync(database)` when application policy allows it. Both return false if the provider supplies no key. Regular queries never invoke the provider.

```csharp
if (await encryption.EnsureUnlockedAsync(database, cancellationToken))
{
    var query = await magicDb.Query<CaseNote>();
    var notes = await query.ToListAsync();
}
await encryption.LockAsync(database);
```

For a locked view, use `SelectPublicAsync(record => record.Title)` on the query or its staged public predicates. It returns the selected values. Use `PatchPublicAsync(new CaseNote { Id = id, Title = title }, record => record.Title)` for one public member. It sends the record key and that value. A full locked read throws `MagicEncryptionLockedException`.

The options bind the account when the Magic service scope is resolved. For an account switch, lock and dispose the old scope and resolve both services from the new account scope. This includes creating explicit app scopes for account switches in standalone WASM. Existing query objects remain bound to the old scope and cannot silently target a different account.

## Query and write behavior

| Operation | Locked behavior | Initial unlocked behavior |
| --- | --- | --- |
| Full entity or protected projection | Recognizable locked error, including empty result sets | Decrypt automatically |
| Explicit public projection | Allowed with public predicates and ordering | Same query plan |
| Public partial update | Allowed if primary key is unchanged and patch touches public members only | Preserve ciphertext byte for byte |
| Full writes, deletes and protected patches | Recognizable locked error | Encrypt before storage |
| Protected predicates and sorts | Recognizable locked error | Reject with a recognizable unsupported-query error until hybrid execution exists |

Projection needs an explicit persisted-member selection so the storage boundary can avoid materializing protected fields. A typed projection must never hand back a partially populated entity that a caller could write over the real value. Map the same property paths as the expression translator, including nested paths descending through a protected property. Keep existing index and cursor behavior for public predicates. Raw ciphertext must never reach the existing expression evaluator as a substitute for the logical value.

For hybrid execution, use public indexed predicates as a candidate filter only when their candidate set is a guaranteed superset of matches. For OR branches, independently produce candidates and union by primary key before residual evaluation. Decrypt after leaving the native candidate transaction or by using bounded batches. Evaluate residual predicates against plaintext before counts and pagination. Apply global ordering and pagination once after union and residual evaluation. Streaming can remain bounded for scan-order queries. Global protected ordering may require materialization. Avoid logging any decrypted row or protected predicate operand.

## Storage and threat model

The browser primitive uses Web Crypto AES-GCM with a 256-bit key, random 96-bit nonces, and a full 128-bit authentication tag. A versioned envelope stores the key identifier, nonce and ciphertext. Associated data binds its version and key ID to the origin, physical database, account, store, actual primary key and protected member or record payload. Primary keys need a canonical representation and must be known before encryption. Start with explicit stable keys and reject auto-increment on protected stores until key allocation can satisfy this requirement. Fresh random nonces give probabilistic uniqueness. Before production, define a per-key invocation budget below the applicable AES-GCM limit across every tab and device that uses that key, and a rotation policy that keeps deployments within that budget.

A protected database should have durable, versioned policy metadata and an authenticated key check. Initialize metadata atomically across tabs. Existing physical databases with missing or mismatched metadata require an explicit migration decision. Policy changes must not silently downgrade protection. Namespace separation keeps older Magic clients writing to the original database during rollout. The application must retire those clients and clear or otherwise handle old data after cutover.

The intent is to protect stored contents when an attacker can inspect or alter browser storage without access to the unlocked key. Envelope authentication detects ciphertext modifications and substitution across authenticated contexts. It cannot detect replay of a previously valid value in the same context. Public indexes, keys, row counts, value lengths, access patterns and storage metadata can remain visible. Same-origin code running while unlocked can access useful data. This design does not provide HIPAA compliance by itself.

## Phases and compatibility

1. Establish the cryptographic format and real-browser tests. Measure encryption and decryption separately from storage and interop.
2. Add scoped key acquisition, versioned policy metadata and locked-state enforcement. Add field and record policy wiring for new databases, and cover every write and read entry point. Expose public projections and narrow patches before allowing locked workflows. Reject unsupported migration, schema and query combinations explicitly.
3. Add explicit migrations and rotation. Prefer copy to a new protected namespace with a verified cutover when converting plaintext. Use resumable checkpoints, idempotent writes and staged key activation. Keep old keys readable while data is rewritten, and retire them only after verification. Define coordinated tab fencing before allowing concurrent migrations. Old browser copies, backups and storage remnants need application handling.
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

Published WebAssembly was exercised with the default Release publish settings.
No AOT performance claim is made. Protected data is isolated in a new physical
namespace. The current version leaves plaintext sources in place and requires
application-managed export, protected import through typed writes, verification,
and cleanup as separate steps.

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
   downgrade, cross-tab migration lock, or live key rotation is provided.
