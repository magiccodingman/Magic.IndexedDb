# Cut over cooperating legacy writers

A native version change closes a connection. A previously shipped 2.0.7 adapter can reopen a compatible store after v3 adoption and write to it. The released-package browser suite keeps that client loaded and checks the resulting row. Only cooperating managed clients recognize and enforce the v3 journal.

## Deploy the cooperation before adoption

A final 2.x bridge application using the released package can wrap **every** IndexedDB write, including background tasks and service-worker queues, in a shared Web Lock. It checks a durable, origin-local stop marker while holding that lock and keeps the lock until the write transaction completes. Propagate a blocked result to the UI and retain pending edits according to application policy.

Before opening v3 storage, the coordinator holds the same lock exclusively. Wait for outstanding writes, verify the complete pinned v3 assets are available and that pending work has been synchronized, exported, or covered by an explicit retention policy. Then persist the marker and start initialization. Other cooperating writers observe the marker after release, even if they resume offline. Leave it set if v3 initialization fails. Retry v3 under the same application gate or offer export/support.

The [runnable gate](../../tests/migrations/ReleasedUpgrade/App/wwwroot/cutover.js), [worker](../../tests/migrations/ReleasedUpgrade/App/wwwroot/cutover-worker.js) and [browser command](../contributing/migration-testing.md#upgrade-the-released-runtime-package-in-a-blazor-app) show the contract. The test denies initialization when prerequisites fail, waits for an ongoing shared write permit, opens v3 after durable fencing, then denies writes from a still-loaded tab and worker while offline.

```js
await legacyWrite(() => saveLegacyTransaction());
await beginCutover(
  () => deploymentReadyAndUnsyncedWorkRetained(),
  () => openManagedStorage()
);
```

Use this example only after all supported writers for the installation have received the bridge. Service workers should stop scheduling legacy writes, use the same gate for writes already queued, and serve only application shells that enforce it once cutover is in effect. Test activation and cache eviction against the app's actual install/update strategy. Merely publishing new service-worker bytes does not revoke JavaScript already running in a tab or worker.

## When the prerequisites cannot be met

Keep v3 storage closed and show an application-specific update, sync or export path. Deployments that cannot establish the prerequisite for previously shipped clients should postpone same-name evolution or design a separate physical namespace and explicit transfer/reconciliation. An acknowledgment or successful preflight covers cooperating clients only. A renamed database leaves late and unsynchronized changes in the old database. The application must decide who reads that database, how late data is synchronized or reconciled, and when it may be retired.

The example covers loaded cooperating tabs and workers while offline. It does not validate a production PWA's installation, update or cache inventory, or find old clients that never received the bridge.
