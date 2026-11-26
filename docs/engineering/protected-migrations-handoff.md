# Authenticated migration implementation and acceptance handoff

The authenticated extension builds on the ordinary migration and recovery
implementation. Both packages use `3.0.0-preview.1`. Run the full acceptance
matrix for the exact release candidate and review the
[v3 license and distribution terms](../licensing/README.md).

## Read first

- [Architecture and invariants](../architecture/adr-authenticated-migrations.md)
- [Application and CLI usage](../guides/encrypted-migrations.md)
- [Portable protocol](../architecture/migration-protocol.md)
- [Recovery contract](../architecture/adr-partial-migration-recovery.md)
- [Existing legacy-writer cutover](../guides/legacy-writer-cutover.md)
- [Test commands and evidence boundaries](../contributing/migration-testing.md)

## What is implemented

The shared JavaScript engine accepts an authenticated, revocable in-memory
adapter. C# remains an authoring and authorization integration; it is not a
dependency of portable migration execution. Static assets contain schemas,
operations, module references, explicit decisions, capabilities and hashes.
Credentials remain application-owned and are never published.

The implementation supports plaintext to encrypted fields, encrypted fields to
plaintext, field to whole-record protection, whole-record to field protection,
and public allowlist changes. `reprotect` performs a policy-only rewrite.
Protection loss requires an explicit `allowProtectionLoss: true` on the affected
store operation. Authenticated data revisions are resumable and permit one data
operation per store. Structural revisions remain atomic.

The same physical database/account identity survives all-public target policies.
`MagicEncryptionOptions.PhysicalDatabaseName` explicitly enrolls an existing
ordinary database in place, with a manifest and reviewed baseline/history.
Enrollment does not migrate between accounts or physical namespaces. An
all-public authenticated database still requires authorization for its identity
and journal; removing the vault's authorization requirement is a separate,
unsupported lifecycle change.

`authenticatedMigrationsV1` gates the portable history. Authenticated journals use
format 3 and carry an encrypted proof of their complete typed state. Admission
installs a policy fence. Crypto runs before the native write transaction; encoded
records, durable cursor/count, and proof commit together. Managed CRUD pins the
ready policy and journal proof. Locking revokes pending work before subsequent
writes. Recovery can repair an interrupted same-key/same-codec transform across
source and target protection partitions without rerunning its faulty hook.

The ordinary path, released-package adoption, historical encryption envelopes,
numeric representations, and existing CRUD/LINQ APIs remain independently tested.
Legacy policy normalization accepts the historical declaration shape without
silently accepting a damaged managed journal as another legacy adoption.

## Verification evidence

GitHub CI runs Chromium, Firefox, Linux WebKit, and macOS WebKit. Use the
exact-candidate JSON/JUnit and published probe reports to distinguish executed
browser and trimmed/AOT coverage from compilation alone. For local setups, see
the [testing guide](../contributing/migration-testing.md).

## Validation procedures

Record the checkout SHA and verify the tests and implementation against the invariants below.

### 1. Execute the mandatory gates on the same final commit

Run the ordinary unit/browser gates, portable protocol/crypto suite, actual
packed-and-installed CLI tests, the configured native migration browser suite,
and the published trimmed/AOT Blazor fixtures. Use all four CI browser
configurations. The runner intentionally fails on missing cases and skipped
required fixture modes. The configured native suite currently requires 128 cases
per browser; the published matrix requires 13 per browser and build mode (104
total). Verify these counts from code and reports, not from earlier approvals.

The [published protected fixture](../../tests/migrations/ProtectedWasm) uses the
installed CLI's exact assets and the actual packed runtime. It adopts field
ciphertext written by the released 2.0.7 adapter on the same origin, locks a
pending C# initialization, retries with fresh authorization,
checks a whole-record ciphertext stage, and then explicitly makes the records
public. It verifies typed CRUD, raw storage, journal v3/history, later unlock,
unavailable and wrong keys, caller versus browser cancellation, account
switching, and sign-out. Two more cases enroll released Lossless plaintext or
adopt a released Lossless vault in place with compound keys, a public index,
exact wire values, and ciphertext inspection. The existing released matrix
tests the actual C# converter and incidental stores separately.

```bash
dotnet test Magic.IndexedDb.UnitTests/Magic.IndexedDb.UnitTests.csproj --configuration Release
node --test tests/migrations/protocol.test.mjs tests/migrations/protected-protocol.test.mjs \
  tests/migrations/protected-adapter.test.mjs tests/migrations/registration.test.mjs
bash .github/scripts/test-migration-cli.sh
dotnet build tests/migrations/BrowserDriver/BrowserDriver.csproj --configuration Release
MAGIC_CLI_FIXTURE=artifacts/migration-cli/published/cli-fixture/manifest.json \
  node tests/migrations/run.mjs chromium \
  tests/migrations/BrowserDriver/bin/Release/net10.0/.playwright/package/index.mjs
MAGIC_UPGRADE_AOT=both bash tests/migrations/ReleasedUpgrade/build.sh artifacts/released-upgrade
```

Follow the testing guide for the pinned old-engine compatibility input and each
released/published fixture run. Repeat relevant browser commands with Firefox
and both supported WebKit hosts. Retain JSON/JUnit, build metadata, logs and
failure artifacts. Verify CI has rerun after the last source change.

### 2. Challenge encryption-specific invariants

Inspect native IndexedDB storage, not just decrypted CRUD results:

- Exercise all six protection transitions, including fields not declared by the
  current model and allowlist growth requiring explicit approval. Check exact
  values, native keys, indexes and target representation after close/reopen.
- Adopt an actual released protected database and enroll an actual released
  plaintext database using the explicit physical binding. Test the application
  provider, account selection and original key with realistic data.
- Test `Legacy` and `Lossless` numeric storage, field converters, supported record
  codecs and representative compound primary keys. Do not convert a numeric
  format or unsupported native key type as part of a protection change.
- Lock, cancel, sign out, dispose the service, terminate the page/process, and
  reauthorize around admission, a committed batch, and finalization. Verify no
  late result commits and no committed transform is replayed.
- Supply a wrong key/account/physical binding, lose authorization, corrupt a
  ciphertext, alter the journal/proof, delete or downgrade the journal, and
  remove metadata entries. Fail before new writes; never recreate a damaged
  managed identity through baseline adoption or `initialize`.
- Keep an old protected tab open during adoption/transition and attempt writes
  from it. Verify the policy fence rejects those writes. Separately exercise
  the documented application-owned cutover for old plaintext writers that never
  checked an encryption sentinel.
- Execute explicit authenticated recovery with one processed target-policy row
  and untouched source-policy rows. Interrupt repair and verification, resume,
  inspect encrypted private recovery evidence, and apply a later ordinary
  revision without losing authenticated recovered history.
- Review event, error, browser-console and artifact output for record contents,
  plaintext secrets, raw keys, provider errors or private checkpoint identities.
  Custom hook failures must remain redacted.

### 3. Test real application rollout and failure conditions

Use static hosting, a pinned deployment manifest, realistic CSP, and the actual
PWA/service-worker cache strategy. Test missing historical assets, offline start,
mixed tabs/workers, account switching, blocked upgrades and stale application
builds. The application owns credentials, UX, telemetry and legacy writer cutover.

Use realistic large datasets and measure startup/migration time. Where feasible,
exercise genuine disk quota exhaustion, process termination and device/browser
restart. Distinguish real failures from injected quota errors and page closure.
If a platform or scenario cannot be tested, report exactly why and whether it is
a release blocker. Do not silently weaken validation, add automatic deletion,
edit published bytes, or force a journal into readiness.

## Scope boundaries

Actual data-key rotation is deliberately rejected with
`MAGDB_KEY_ROTATION_UNSUPPORTED`. A future implementation needs source/target key
roles, two authorized leases, mixed-key recovery and explicit key-retirement
semantics. Rewrapping the same data key belongs to the provider. Do not label
policy reprotection as key rotation.

Native key/index members must remain public and top-level for authenticated
schemas. Store renames, primary-key changes, structural staging/swap,
cross-database moves, numeric-format changes and repair of a failed recovery
remain unsupported. A whole internally consistent storage-snapshot replay cannot
be detected without an external monotonic authority. Preserve these boundaries
and call out any proposed API/protocol expansion for independent review.
