import { commitRotationBatch, readRotationBatch } from "./rotationBatch.js";
import { encryptionMetadataStore } from "./encryptionPolicy.js";
import { rotationJournalKey, sealRotationJournal, verifyRotationJournal } from "./rotationJournal.js";
import { openRotatingRecord, prepareRotatedRecord } from "./rotationRecord.js";

// The caller acquires an authenticated ready-state snapshot and the vault
// coordinator. This step still compares persisted state inside the write
// transaction because an advisory coordinator cannot fence other writers.
export async function advanceRotationPass({
    db, vault, document, keys, policies, expectations, batchSize, assertCurrent, signal
}) {
    signal?.throwIfAborted();
    const state = await verifyRotationJournal(document, keys, vault, assertCurrent);
    if (state.paused) return { outcome: "paused", document };

    const stores = [...policies].filter(([, policy]) => policy.exposed !== null || policy.fields.size > 0);
    if (state.storeIndex > stores.length || state.storeIndex === stores.length && state.afterKey !== null) {
        throw new Error("MAGIC_ROTATION_STATE_INVALID: The rotation cursor is outside the authenticated model.");
    }
    if (state.storeIndex === stores.length) return { outcome: "pass-complete", document };

    const [store, policy] = stores[state.storeIndex];
    const rows = await readRotationBatch(db, store, state.afterKey, batchSize);
    const comparisons = [...expectations, { store: encryptionMetadataStore, key: rotationJournalKey, value: document }];
    const writes = [];
    let verifiedCount = 0;
    const verificationKeys = state.phase === "verifying" ? new Map([[state.targetKeyId, keys.get(state.targetKeyId)]]) : null;
    for (const row of rows) {
        signal?.throwIfAborted();
        assertCurrent();
        const context = { ...vault, store, primaryKey: row.key };
        if (verificationKeys) {
            await openRotatingRecord(row.record, policy, verificationKeys, context);
            verifiedCount++;
        } else {
            const converted = await prepareRotatedRecord(row.record, policy, keys, state.targetKeyId, context);
            if (converted !== null) writes.push({ store, key: row.key, value: converted });
        }
        comparisons.push({ store, key: row.key, value: row.record });
    }

    const convertedCount = writes.length;
    const next = {
        ...state,
        sequence: state.sequence + 1,
        storeIndex: state.storeIndex + (rows.length === 0 ? 1 : 0),
        afterKey: rows.length ? rows.at(-1).key : null,
        converted: state.converted + convertedCount,
        verified: state.verified + verifiedCount
    };
    const prepared = await sealRotationJournal(next, keys, vault, assertCurrent);
    writes.push({ store: encryptionMetadataStore, key: rotationJournalKey, value: prepared });
    if (!await commitRotationBatch(db, comparisons, writes, assertCurrent, signal)) {
        return { outcome: "retry", document };
    }
    return { outcome: "advanced", document: prepared };
}
