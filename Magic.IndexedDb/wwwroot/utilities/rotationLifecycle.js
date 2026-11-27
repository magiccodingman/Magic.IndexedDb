import { assertDatabaseReady, assertNumericPolicy, inspectOpen } from "../migrations/migrationEngine.js";
import { canonicalJson, metadataStore, samePhysical, sha256 } from "../migrations/protocol.js";
import { createProtectedMigrationAdapter, protectedStateKey, recoveryEvidenceKey, retainedRecoveries } from "../migrations/protectedMigrationAdapter.js";
import { encryptValue } from "./encryptionEnvelope.js";
import { serializePolicyDocument } from "./encryptionPolicyDocument.js";
import { encryptionMetadataKey, encryptionMetadataStore } from "./encryptionPolicy.js";
import { numericMetadataStore } from "./numericStorage.js";
import { portableProtectionPolicy } from "./protectionPolicy.js";
import { commitRotationBatch, readRotationEntries } from "./rotationBatch.js";
import { rotationPassCount } from "./rotationPass.js";
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
    db, vault, ready, schema, authorization, keys, targetKeyId, mode, recoverable, signal
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
    if (sourceKeyId === targetKeyId) {
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
    const priorKeyIds = sourceProof.keyLineage?.generations ?? [sourceKeyId];
    if (priorKeyIds.includes(targetKeyId))
        throw new Error("MAGIC_ROTATION_KEY_REUSED: The target key identifier was previously used.");
    await assertDistinctRotationKeys(source, target, sourceKeyId, vault, assertCurrent);
    const { policyFingerprint } = portableProtectionPolicy(schema);
    const state = {
        version: 1, rotationId: crypto.randomUUID(), sourceKeyId, targetKeyId, mode, writeKeyId: targetKeyId,
        modelHash: ready.schemaHash, policyFingerprint, priorKeyIds, sequence: 0, phase: "converting",
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

export async function enterRotationVerification({ db, vault, ready, schema, document, fence, checks, keys, assertCurrent, signal }) {
    const state = await verifyRotationJournal(document, keys, vault, assertCurrent);
    const target = keys.get(state.targetKeyId);
    await verifyRotationFence(fence, state, target, vault, assertCurrent);
    const { policies } = portableProtectionPolicy(schema);
    const storeCount = rotationPassCount(policies, ready);
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

export async function finishRotation({
    db, vault, ready, schema, document, fence, checks, keys, assertCurrent, signal
}) {
    const state = await verifyRotationJournal(document, keys, vault, assertCurrent);
    const target = keys.get(state.targetKeyId);
    await verifyRotationFence(fence, state, target, vault, assertCurrent);
    const { policies, policyFingerprint } = portableProtectionPolicy(schema);
    if (state.phase !== "verifying" || state.storeIndex !== rotationPassCount(policies, ready) || state.afterKey !== null ||
        state.paused || state.policyFingerprint !== policyFingerprint || state.modelHash !== ready.schemaHash) {
        throw new Error("MAGIC_ROTATION_STATE_INVALID: Target-only verification is incomplete.");
    }
    const lineage = {
        generations: [...state.priorKeyIds, state.targetKeyId],
        completed: { rotationId: state.rotationId, converted: state.converted, verified: state.verified }
    };
    const adapter = await createProtectedMigrationAdapter({
        name: vault.database, account: vault.account, keyId: state.targetKeyId, key: target, assertCurrent, signal
    });
    // Compare target-authenticated audit envelopes in the final transaction
    // too, so removing or replacing a verified audit cannot retire its key.
    const recoveries = retainedRecoveries(ready);
    const auditKeys = recoveries.map(entry => recoveryEvidenceKey(entry.recovery.id));
    const audits = auditKeys.length ? await readRotationEntries(db, metadataStore, auditKeys) : [];
    const auditChecks = [];
    for (const [index, entry] of recoveries.entries()) {
        signal?.throwIfAborted();
        await adapter.verifyRecoveryEvidence(audits[index], entry);
        auditChecks.push({ store: metadataStore, key: auditKeys[index], value: audits[index] });
    }

    const [policySeal, journalSeal] = await Promise.all([
        encryptValue(serializePolicyDocument(policyFingerprint, lineage), target, state.targetKeyId,
            { ...vault, store: encryptionMetadataStore, primaryKey: encryptionMetadataKey, member: null }),
        adapter.sealState(ready.journal)
    ]);
    if (!await commitRotationBatch(db, [...checks, ...auditChecks, policyEntry(fence), rotationEntry(document)],
        [policyEntry(policySeal), { store: encryptionMetadataStore, key: protectedStateKey, value: journalSeal },
            { store: encryptionMetadataStore, key: rotationJournalKey, delete: true }], assertCurrent, signal)) {
        throw new Error("MAGIC_ROTATION_CONCURRENT_CHANGE: The vault changed before finalization.");
    }
    return { ready: { ...ready, policySeal, journalSeal }, lineage };
}

export async function openRotation({ db, vault, schema, authorization, keys }) {
    const { assertCurrent } = authorization;
    const snapshot = await inspectOpen(db, rotationJournalKey);
    if (!snapshot.rotation) return null;
    const state = await verifyRotationJournal(snapshot.rotation, keys, vault, assertCurrent);
    const { policyFingerprint } = portableProtectionPolicy(schema);
    const expectedHash = await sha256(canonicalJson(schema));
    if (db.name !== vault.database || authorization.account !== vault.account ||
        state.sourceKeyId !== authorization.keyId || snapshot.state?.status !== "ready" ||
        snapshot.state.protocolVersion !== 3 || snapshot.state.schemaId !== schema.schemaId ||
        state.modelHash !== expectedHash || state.modelHash !== snapshot.state.schemaHash ||
        state.policyFingerprint !== policyFingerprint || !samePhysical(snapshot.stores, schema)) {
        throw new Error("MAGIC_ROTATION_STATE_INVALID: The rotation does not match the installed vault model.");
    }
    assertNumericPolicy(snapshot, schema);
    await authorization.verifyState(snapshot.protectionState, snapshot.state);
    await verifyRotationFence(snapshot.protectionPolicy, state, keys.get(state.targetKeyId), vault, assertCurrent);

    const ready = {
        name: db.name, schemaId: schema.schemaId, revision: snapshot.state.revision, schemaHash: expectedHash,
        nativeVersion: snapshot.nativeVersion, protocolVersion: 3, history: snapshot.state.history,
        journal: snapshot.state, journalSeal: snapshot.protectionState, numericPolicy: snapshot.numericPolicy,
        policySeal: snapshot.protectionPolicy
    };
    return { ready, document: snapshot.rotation, fence: snapshot.protectionPolicy, checks: unchangedReadyMetadata(db, ready) };
}

export async function setRotationPaused({ db, vault, document, fence, checks, keys, paused, assertCurrent, signal }) {
    const state = await verifyRotationJournal(document, keys, vault, assertCurrent);
    if (state.paused === paused) return { document, fence, checks };
    const next = await sealRotationJournal({ ...state, paused, sequence: state.sequence + 1 }, keys, vault, assertCurrent);
    if (!await commitRotationBatch(db, [...checks, policyEntry(fence), rotationEntry(document)],
        [rotationEntry(next)], assertCurrent, signal)) {
        throw new Error("MAGIC_ROTATION_CONCURRENT_CHANGE: The vault changed before rotation pause or resume.");
    }
    return { document: next, fence, checks };
}
