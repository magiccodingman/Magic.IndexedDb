# Encryption at rest

This design covers application-level encryption of IndexedDB data. The first milestone establishes the browser encryption primitive and its authenticated storage format. The API sketches below describe the intended integration.

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

## Developer experience sketch

An application can configure one asynchronous provider in DI. The provider can look up the account's wrapped data key, unwrap it using an application-selected mechanism, and return it with the requested key ID. Password providers derive or unwrap the data key before returning it. The provider never needs to send a password or WebAuthn assertion to the database library. Interactive providers should distinguish an unavailable interaction from an incorrect key.

```csharp
// Proposed surface
services.AddMagicIndexedDbEncryption<AppKeyProvider>();

public sealed class CaseNoteTable // Table mapping omitted from this sketch.
{
    public Guid Id { get; set; }
    [MagicEncrypt] public string Note { get; set; } = "";
}

// Only routes that need protected data ask for an unlock.
var result = await encryption.EnsureUnlockedAsync(database, cancellationToken);
if (result == MagicUnlockResult.Unlocked)
{
    var rows = await (await magicDb.Query<CaseNoteTable>()).ToListAsync();
}
```

A production API must supply the account-bound database descriptor to both the query and unlock paths. The account is a routing identity. The consuming app authenticates it and supplies the associated key. Lock is local to the app instance. A consumer that needs coordinated sign-out across tabs must signal each app context and invoke lock there. Page disposal should clear only keys owned by that instance.

## Query and write behavior

| Operation | Locked behavior | Initial unlocked behavior |
| --- | --- | --- |
| Full entity or protected projection | Recognizable locked error, including empty result sets | Decrypt automatically |
| Explicit public projection | Allowed with public predicates and ordering | Same query plan |
| Public partial update | Allowed if primary key is unchanged and patch touches public members only | Preserve ciphertext byte for byte |
| Full writes and protected patches | Recognizable locked error | Encrypt before storage |
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
