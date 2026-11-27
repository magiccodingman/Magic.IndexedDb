import { metadataStore } from "../migrations/protocol.js";
import { createProtectedMigrationAdapter, recoveryEvidenceKey, retainedRecoveries } from "../migrations/protectedMigrationAdapter.js";
import { commitRotationBatch, readRotationBatch, readRotationEntries } from "./rotationBatch.js";
import { encryptionMetadataStore } from "./encryptionPolicy.js";
import { rotationJournalKey, sealRotationJournal, verifyRotationJournal } from "./rotationJournal.js";
import { openRotatingRecord, prepareRotatedRecord } from "./rotationRecord.js";

const protectedStores = policies => [...policies].filter(([, policy]) => policy.exposed !== null || policy.fields.size > 0);

const hasRecoveryEvidence = ready => ready?.history.some(entry => entry.outcome === "recovered") ?? false;

export function rotationPassCount(policies, ready) {
    return protectedStores(policies).length + Number(hasRecoveryEvidence(ready));
}

async function prepareRecoveryEvidence({ db, ready, vault, state, keys, assertCurrent, signal, batchSize }) {
    const entries = retainedRecoveries(ready).map(entry => ({ entry, key: recoveryEvidenceKey(entry.recovery.id) }));
    entries.sort((left, right) => left.key < right.key ? -1 : Number(left.key > right.key));
    const start = state.afterKey === null ? 0 : entries.findIndex(item => item.key === state.afterKey) + 1;
    if (state.afterKey !== null && start === 0) throw new Error("MAGIC_ROTATION_STATE_INVALID: Invalid recovery evidence checkpoint.");
    const batch = entries.slice(start, start + batchSize);
    if (!batch.length) return { afterKey: null, comparisons: [], writes: [] };
    const adapter = keyId => createProtectedMigrationAdapter({ name: vault.database, account: vault.account,
        keyId, key: keys.get(keyId), assertCurrent, signal });
    const target = await adapter(state.targetKeyId);
    const source = state.phase === "converting" ? await adapter(state.sourceKeyId) : null;
    const records = await readRotationEntries(db, metadataStore, batch.map(item => item.key));
    const comparisons = [];
    const writes = [];
    for (const [index, { entry, key }] of batch.entries()) {
        signal?.throwIfAborted();
        const value = records[index];
        comparisons.push({ store: metadataStore, key, value });
        if (source && value?.keyId !== state.targetKeyId) {
            writes.push({ store: metadataStore, key, value: await target.rotateRecoveryEvidence(value, entry, source) });
        } else {
            await target.verifyRecoveryEvidence(value, entry);
        }
    }
    return { afterKey: batch.at(-1).key, comparisons, writes };
}

// The caller acquires an authenticated ready-state snapshot and the vault
// coordinator. This step still compares persisted state inside the write
// transaction because an advisory coordinator cannot fence other writers.
export async function advanceRotationPass({
    db, vault, document, keys, policies, ready, expectations, batchSize, assertCurrent, signal
}) {
    signal?.throwIfAborted();
    const state = await verifyRotationJournal(document, keys, vault, assertCurrent);
    if (state.paused) return { outcome: "paused", document };

    const stores = protectedStores(policies);
    const count = stores.length + Number(hasRecoveryEvidence(ready));
    if (state.storeIndex > count || state.storeIndex === count && state.afterKey !== null) {
        throw new Error("MAGIC_ROTATION_STATE_INVALID: The rotation cursor is outside the authenticated model.");
    }
    if (state.storeIndex === count) return { outcome: "pass-complete", document };

    const evidence = state.storeIndex === stores.length;
    const batch = evidence
        ? await prepareRecoveryEvidence({ db, ready, vault, state, keys, assertCurrent, signal, batchSize })
        : await prepareApplicationRows({ db, vault, state, keys, batchSize, assertCurrent, signal,
            storePolicy: stores[state.storeIndex] });
    const comparisons = [...expectations, { store: encryptionMetadataStore, key: rotationJournalKey, value: document },
        ...batch.comparisons];
    const next = {
        ...state,
        sequence: state.sequence + 1,
        storeIndex: state.storeIndex + (batch.afterKey === null ? 1 : 0),
        afterKey: batch.afterKey,
        converted: state.converted + (evidence ? 0 : batch.writes.length),
        verified: state.verified + (evidence ? 0 : batch.verified)
    };
    const writes = batch.writes;
    const prepared = await sealRotationJournal(next, keys, vault, assertCurrent);
    writes.push({ store: encryptionMetadataStore, key: rotationJournalKey, value: prepared });
    if (!await commitRotationBatch(db, comparisons, writes, assertCurrent, signal)) {
        return { outcome: "retry", document };
    }
    return { outcome: "advanced", document: prepared };
}

async function prepareApplicationRows({ db, vault, state, keys, batchSize, assertCurrent, signal, storePolicy }) {
    const [store, policy] = storePolicy;
    const rows = await readRotationBatch(db, store, state.afterKey, batchSize);
    const comparisons = [];
    const writes = [];
    let verified = 0;
    const verificationKeys = state.phase === "verifying" ? new Map([[state.targetKeyId, keys.get(state.targetKeyId)]]) : null;
    for (const row of rows) {
        signal?.throwIfAborted();
        assertCurrent();
        const context = { ...vault, store, primaryKey: row.key };
        if (verificationKeys) {
            await openRotatingRecord(row.record, policy, verificationKeys, context);
            verified++;
        } else {
            const converted = await prepareRotatedRecord(row.record, policy, keys, state.targetKeyId, context);
            if (converted !== null) writes.push({ store, key: row.key, value: converted });
        }
        comparisons.push({ store, key: row.key, value: row.record });
    }
    return { afterKey: rows.length ? rows.at(-1).key : null, comparisons, writes, verified };
}
