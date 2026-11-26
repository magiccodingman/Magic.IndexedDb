# Encrypted migrations

Version 3 connects managed migrations to the existing authenticated
encryption lifecycle. The C# model and CLI describe the intended storage policy;
the shared JavaScript engine performs the transition after the application
provides authorization. Review the [design contract](../architecture/adr-authenticated-migrations.md)
and [real-environment acceptance handoff](../engineering/protected-migrations-handoff.md)
before promoting this extension to a stable release.

## Author the model and history

Continue using `MagicEncrypt` for fields and `MagicEncryptRecord` with an explicit
public allowlist for whole-record protection. Use the same encryption and numeric
configuration in the application and its design-time exporter. Authenticated
database schemas declare `authenticatedMigrationsV1`, including a vault whose
current fields are all public.

```sh
magicdb migrations add ProtectNotes --project tools/App.Schema/App.Schema.csproj --database records
magicdb migrations validate --project tools/App.Schema/App.Schema.csproj --database records --json
magicdb migrations publish --project tools/App.Schema/App.Schema.csproj --database records --output wwwroot/db/releases/app-004
```

A protection-only change generates `reprotect`. Removing protection requires a
reviewed `allowProtectionLoss: true` decision on that store's operation. This
includes expanding a whole-record public allowlist. An unresolved decision blocks
validation and publication. A developer does not write AES, nonce, or ciphertext
manipulation code for these transitions.

Authenticated record operations use resumable mode. There is one logical data
operation per store per revision. Put a combined rename/type/backfill/protection
change into one custom transform when built-ins cannot describe it. Put structural
changes, such as adding a public index, into adjacent atomic revisions. The CLI
and runtime reject combinations that cannot safely execute under these rules.

## Use the existing C# authorization lifecycle

Pin the same static manifest in the repository declaration:

```csharp
public static readonly IndexedDbSet Records = new IndexedDbSet("records")
    .WithSchemaId("records")
    .WithMigrations("db/releases/app-004/records/manifest.json");
```

Configure the existing encryption provider for the logical database and account:

```csharp
services.AddMagicIndexedDbEncryption<MyKeyProvider>(_ =>
[
    new MagicEncryptionOptions("records", accountScope, keyId)
]);
```

For an existing authenticated database, use `IMagicIndexedDbEncryptionLifecycle`:

```csharp
await encryption.EnsureUnlockedAsync(AppDatabases.Records);
var database = await magicDb.Database(AppDatabases.Records);
await database.OpenAsync();
```

For a genuinely new vault, or an explicitly reviewed first enrollment of plaintext
storage, call `InitializeAsync` instead. An unavailable key leaves storage
unmodified. An incorrect key fails authentication. Initialization never replaces
a missing/corrupted policy in an already authenticated vault.

The provider decides how to obtain the key. Magic does not choose a PIN, prompt
for biometrics, contact a server, or persist the key. `LockAsync` revokes ongoing
migration authorization and prevents subsequent commits. Already committed batches
remain durable and can resume after authorization is obtained again.

## Encrypt an existing plaintext database in place

The default encrypted physical name is account-scoped. Adding encryption
configuration does not automatically copy or rename an existing ordinary
database. To deliberately enroll that exact physical database, bind it explicitly:

```csharp
new MagicEncryptionOptions("records", accountScope, keyId)
{
    PhysicalDatabaseName = "existing-browser-db"
}
```

This binding requires a pinned manifest. For unmanaged data, author a baseline
that exactly matches the old layout and data contract, then add the protection
change as a separate revision. Call `InitializeAsync` to establish the initial
authenticated identity. Choose the physical name and account binding from the
application's known deployment; a matching native layout alone does not establish
ownership or record semantics.

Keep the encryption configuration and binding after every field becomes public.
The records then follow the public target policy, while the database retains its
authenticated lifecycle, history, account identity, and ability to encrypt fields
again later. Do not remove the encryption context to bypass readiness.

Retire or fence older plaintext writers before enrollment. Previously shipped code
that bypasses Magic's new guards can still use native IndexedDB. See the
[legacy-writer cutover guide](legacy-writer-cutover.md).

## Direct JavaScript use

The portable engine takes the same static assets and an ephemeral adapter:

```javascript
import { createProtectedMigrationAdapter } from "./migrations/protectedMigrationAdapter.js";
import { ensureDatabase } from "./migrations/migrationEngine.js";

const authorization = new AbortController();
const protection = await createProtectedMigrationAdapter({
    name: physicalDatabaseName,
    account: accountScope,
    keyId,
    key: authorizedCryptoKey,
    signal: authorization.signal
});

await ensureDatabase({
    name: physicalDatabaseName,
    schema: targetSchema,
    manifestUrl: pinnedManifestUrl,
    protection,
    initialize: false
});
```

The returned readiness result is for this model and instance. Use the managed
adapter for application CRUD; do not run an independent writer alongside a
migration. The higher-level JavaScript registration API separates key authorization
from migration execution: `unlockProtectedDatabase` followed by
`ensureProtectedMigration`. The C# lifecycle performs those stages for callers.

## Inspection, events, and emergency recovery

Existing migration observation and inspection APIs still apply. Authorization
events use `authorizationRequired`, `authorizationRejected`, and
`authorizationLost`. Progress reports committed counts; record contents and
private cursor keys are excluded. The app chooses whether to prompt, retry, show
support instructions, or forward metadata to telemetry.

For a faulty partially committed custom transform, author an approved recovery
package using the [recovery workflow](migration-cli.md). Then:

```csharp
var database = await magicDb.Database(AppDatabases.Records);
await database.RecoverMigrationAsync(recoveryId, recoveryDescriptorDigest);
await database.OpenAsync();
```

Recovery acquires authorization directly; it does not first rerun the failing
original transform. It repairs the interrupted edge, verifies the repaired
records, and commits its recorded outcome. Normal opening subsequently advances
later revisions. Both processed and untouched partitions are decoded under their
appropriate source/target policies.

Authorization failure, cancellation, quota failure, tampered metadata, or a failed
verifier does not trigger automatic deletion or forced completion. Diagnose the
reported state and use the original immutable package or an explicitly approved
recovery. The engine cannot reconstruct information already lost by application
code.

## Current boundaries

The current adapter uses one data-encryption key throughout migration. Actual
data-key rotation requires a separate two-key protocol/provider extension and is
explicitly rejected. Rewrapping the same data key after a PIN change belongs to
the application's provider.

Native key/index members remain public and use the existing supported context
rules. Resumable key changes, authenticated store renames, cross-database copies,
numeric-format conversion, and repair of a failed recovery are not implemented.
These guards are intentional; do not weaken them to make a deployment proceed.
