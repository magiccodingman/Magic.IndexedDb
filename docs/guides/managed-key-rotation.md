# Rotate a managed database's encryption key

Use key rotation after opening an authenticated managed database. Rotation does
not change the schema revision, move the database, or rewrite its public keys and
indexes. Rewrapping the same data key with a new password, PIN, or wrapping key is
an application operation and does not require this API.

## C# quick start

Implement `IMagicEncryptionRotationKeyProvider` on the application's existing
provider. The same provider should resolve both IDs and attest that it can
reacquire both keys after a browser restart. The library checks the supplied keys
against authenticated vault state. The provider controls user authorization,
wrapped key persistence, backups and permanent retirement.

```csharp
await context.Encryption.EnsureUnlockedAsync(database);
var result = await context.KeyRotation.RotateEncryptionKeyAsync(
    database, new("generation-2", MagicKeyRotationMode.Online, BatchSize: 128));

if (result.RetirementReady)
{
    // Apply the application's backup and retirement policy for result.SourceKeyId.
}
```

Use opaque browser AES-GCM keys when feasible. Raw 32-byte provider values are
accepted through the existing key wrapper. Neither raw keys nor decrypted values
are written to rotation metadata or progress diagnostics.

## Choose availability

- **Exclusive** is the default. Ordinary operations on this vault wait for the
  application to finish or resume rotation.
- **ReadOnly** preserves authorized reads while both generations coexist. Record
  mutations, including deletes and public-field patches, reject.
- **Online** preserves authorized reads and mutations during conversion. New
  writes use the target key. A short final verification barrier rejects writes in
  all modes. Retry an interrupted write after the vault is available.

Application code that receives a rotation fence or authenticated readiness
change can reauthorize and retry.
A stream invalidated by authorization or a phase change should restart. Unrelated
vaults continue operating. Retire raw or otherwise uncooperative IndexedDB writers
before starting. Use the [legacy writer cutover](legacy-writer-cutover.md) when
deploying to an origin with older application shells.

## Observe, cancel, and resume

`GetKeyRotationStatusAsync` reports the authenticated phase, available operations,
committed conversion and verification totals, and retirement readiness. Counts
are committed work totals. They are not an estimate of remaining records during
online rotation.

`CancelKeyRotationAsync` aborts work in the current browser context. It preserves
committed checkpoints and availability. `PauseKeyRotationAsync` persists the pause.
When ready to continue, unlock with the rotation-capable provider and call
`ResumeKeyRotationAsync`. Set `MagicEncryptionOptions.ResumeKeyRotationOnUnlock`
to schedule background resumption on unlock. An explicit pause still requires
an explicit resume. Existing `ObserveMigrationsAsync` subscriptions receive
`key-rotation` events with metadata in `Details["rotation"]`.

A .NET caller's cancellation token cancels its wait.
Lock the vault to revoke browser authorization and pending work. Missing keys and
invalid authentication leave the vault fenced until correct authorization is available.

Rotation is offline when the existing managed registration and application-owned
key-provider dependencies are available offline. The application owns recovery if
a required key is lost. Do not destroy either key while the rotation is incomplete.

## JavaScript

The browser APIs use the logical route registered by `createDb`. Unlock and
initialize the managed vault before calling `rotateManagedEncryptionKey`.

```js
const stop = api.subscribeKeyRotationEvents(({ phase, converted }) => {
    console.log(phase, converted);
});
try {
    const status = await api.rotateManagedEncryptionKey(route,
        { targetKeyId, mode: "online", batchSize: 128, recoverable: true }, targetKey);
    // status.retirementReady applies to the active managed vault.
} finally {
    stop();
}
```

After reload, get routing hints with `getManagedEncryptionKeyHints`, independently
authorize the requested keys, then supply them with `authorizeManagedKeyRotation`.
Hints are untrusted until the browser authenticates the journal and policy fence.
Call `resumeManagedEncryptionKeyRotation` to continue. Use
`pauseManagedEncryptionKeyRotation` or `cancelManagedEncryptionKeyRotation` to stop
the current attempt. Progress events report metadata only, after commit.

## Retirement and tuning

Retirement readiness follows a verified target-only scan and atomic finalization.
It applies to the active vault. Retain old keys required by backups and exports.
The browser cannot prove physical erasure or detect replay of a complete consistent
storage snapshot without an external trusted freshness anchor.

Rotation compares portable stored records, including native dates and byte arrays.
Unsupported values written by external IndexedDB clients, such as cyclic
structures or maps, stop conversion without committing the affected batch. Keep
both keys and address the incompatible data with the application’s recovery policy.

Start with 128 records per batch. Reduce the batch size for large records or
foreground contention, and enable yielding if the application needs more
responsiveness. Larger batches can reduce transaction overhead but increase
preparation memory and retry work. Rotation and schema migration serialize on the
physical vault. Complete rotation from the admitting deployment before applying a newer manifest or managed schema.
See the [architecture contract](../architecture/adr-managed-key-rotation.md).
