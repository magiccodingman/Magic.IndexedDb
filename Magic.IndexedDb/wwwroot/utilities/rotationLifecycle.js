import { assertDatabaseReady } from "../migrations/migrationEngine.js";
import { metadataStore } from "../migrations/protocol.js";
import { createProtectedMigrationAdapter, protectedStateKey } from "../migrations/protectedMigrationAdapter.js";
import { encryptionMetadataKey, encryptionMetadataStore } from "./encryptionPolicy.js";
import { numericMetadataStore } from "./numericStorage.js";
import { portableProtectionPolicy } from "./protectionPolicy.js";
import { commitRotationBatch } from "./rotationBatch.js";
import { assertDistinctRotationKeys, sealRotationFence, verifyRotationFence } from "./rotationFence.js";
import { rotationJournalKey, sealRotationJournal, verifyRotationJournal } from "./rotationJournal.js";

const policyEntry = value => ({ store: encryptionMetadataStore, key: encryptionMetadataKey, value });
const rotationEntry = value => ({ store: encryptionMetadataStore, key: rotationJournalKey, value });

function unchangedReadyMetadata(db, ready) {
    const checks = [
        { store: metadataStore, key: "state", value: ready.journal },
        { store: encryptionMetadataStore, key: protectedStateKey, value: ready.journalSeal }
    ];
    if (db.objectStoreNames.contains(numericMetadataStore)) {
        checks.push({ store: numericMetadataStore, key: "policy", value: ready.numericPolicy });
    }
    return checks;
}

export function rotationCoordinator(db, signal, operation) {
    if (!navigator.locks?.request) throw new Error("MAGIC_ROTATION_LOCK_UNAVAILABLE: Web Locks are required for managed rotation.");
    // Schema migration and rotation serialize on the physical database, while
    // ordinary operations enforce the persisted admission policy independently.
    return navigator.locks.request(`magic-indexeddb:migrate:${db.name}`, { mode: "exclusive", signal }, operation);
}

export async function admitRotation({
    db, vault, ready, schema, authorization, keys, targetKeyId, mode, recoverable, priorKeyIds, signal
}) {
    const { assertCurrent, keyId: sourceKeyId } = authorization;
    signal?.throwIfAborted();
    assertCurrent();
    if (db.name !== vault.database || authorization.account !== vault.account) {
        throw new Error("MAGIC_ROTATION_STATE_INVALID: The authorization belongs to another vault.");
    }
    if (recoverable !== true) {
        throw new Error("MAGIC_ROTATION_KEYS_NOT_RECOVERABLE: The provider must attest recovery of both keys.");
    }
    if (sourceKeyId === targetKeyId || priorKeyIds.has(targetKeyId)) {
        throw new Error("MAGIC_ROTATION_KEY_REUSED: The target key identifier was previously used.");
    }
    const source = keys.get(sourceKeyId);
    const target = keys.get(targetKeyId);
    if (!source || !target) throw new Error("MAGIC_ROTATION_KEY_UNAVAILABLE: Both authorized keys are required.");

    await assertDatabaseReady(db, ready, authorization);
    const sourceProof = await createProtectedMigrationAdapter({
        name: vault.database, account: vault.account, keyId: sourceKeyId, key: source, assertCurrent, signal
    });
    await sourceProof.verifyPolicy(ready.policySeal, schema);
    await assertDistinctRotationKeys(source, target, sourceKeyId, vault, assertCurrent);
    const { policyFingerprint } = portableProtectionPolicy(schema);
    const state = {
        version: 1, rotationId: crypto.randomUUID(), sourceKeyId, targetKeyId, mode, writeKeyId: targetKeyId,
        modelHash: ready.schemaHash, policyFingerprint, sequence: 0, phase: "converting",
        storeIndex: 0, afterKey: null, converted: 0, verified: 0, paused: false
    };
    const [document, fence] = await Promise.all([
        sealRotationJournal(state, keys, vault, assertCurrent),
        sealRotationFence(state, target, vault, assertCurrent)
    ]);
    const checks = unchangedReadyMetadata(db, ready);
    if (!await commitRotationBatch(db, [...checks, policyEntry(ready.policySeal), rotationEntry(undefined)],
        [policyEntry(fence), rotationEntry(document)], assertCurrent, signal)) {
        throw new Error("MAGIC_ROTATION_CONCURRENT_CHANGE: The vault changed before rotation admission.");
    }
    return { document, fence, checks };
}

export async function enterRotationVerification({ db, vault, document, fence, checks, keys, storeCount, assertCurrent, signal }) {
    const state = await verifyRotationJournal(document, keys, vault, assertCurrent);
    const target = keys.get(state.targetKeyId);
    await verifyRotationFence(fence, state, target, vault, assertCurrent);
    if (state.phase !== "converting" || state.storeIndex !== storeCount || state.afterKey !== null || state.paused) {
        throw new Error("MAGIC_ROTATION_STATE_INVALID: Conversion has not completed.");
    }

    const verifying = { ...state, phase: "verifying", sequence: state.sequence + 1, storeIndex: 0 };
    const [prepared, barrier] = await Promise.all([
        sealRotationJournal(verifying, keys, vault, assertCurrent),
        sealRotationFence(verifying, target, vault, assertCurrent)
    ]);
    if (!await commitRotationBatch(db, [...checks, policyEntry(fence), rotationEntry(document)],
        [policyEntry(barrier), rotationEntry(prepared)], assertCurrent, signal)) {
        throw new Error("MAGIC_ROTATION_CONCURRENT_CHANGE: The vault changed before verification.");
    }
    return { document: prepared, fence: barrier, checks };
}
