import { canonicalJson } from "../migrations/protocol.js";
import { encryptValue, decryptValue } from "./encryptionEnvelope.js";
import { encryptionMetadataStore } from "./encryptionPolicy.js";

export const rotationJournalKey = "key-rotation";
const stateFields = new Set([
    "version", "rotationId", "sourceKeyId", "targetKeyId", "mode", "modelHash", "policyFingerprint",
    "writeKeyId", "priorKeyIds", "sequence", "phase", "storeIndex", "afterKey", "converted", "verified", "paused"
]);
const modes = new Set(["exclusive", "read-only", "online"]);
const phases = new Set(["converting", "verifying"]);
const nonempty = value => typeof value === "string" && value.length > 0;
const natural = value => Number.isSafeInteger(value) && value >= 0;
const keyPart = value => typeof value === "string" || Number.isFinite(value);
const checkpoint = value => value === null || keyPart(value) ||
    Array.isArray(value) && value.length > 0 && value.every(keyPart);

function invalidJournal() {
    return new Error("MAGIC_ROTATION_STATE_INVALID: The rotation journal could not be authenticated.");
}

function validateState(state) {
    if (!state || Object.getPrototypeOf(state) !== Object.prototype ||
        Object.keys(state).length !== stateFields.size || Object.keys(state).some(name => !stateFields.has(name)) ||
        state.version !== 1 || !nonempty(state.rotationId) || !nonempty(state.sourceKeyId) ||
        !nonempty(state.targetKeyId) || state.sourceKeyId === state.targetKeyId ||
        !modes.has(state.mode) || !phases.has(state.phase) || state.writeKeyId !== state.targetKeyId ||
        !Array.isArray(state.priorKeyIds) || !state.priorKeyIds.every(nonempty) ||
        new Set(state.priorKeyIds).size !== state.priorKeyIds.length ||
        state.priorKeyIds.at(-1) !== state.sourceKeyId || state.priorKeyIds.includes(state.targetKeyId) ||
        !nonempty(state.modelHash) || !nonempty(state.policyFingerprint) ||
        ![state.sequence, state.storeIndex, state.converted, state.verified].every(natural) ||
        !checkpoint(state.afterKey) || typeof state.paused !== "boolean" ||
        state.phase === "converting" && state.verified !== 0) throw invalidJournal();
}

function context(vault, member) {
    return { ...vault, store: encryptionMetadataStore, primaryKey: rotationJournalKey, member };
}

function canonicalState(state) {
    validateState(state);
    return canonicalJson(["Magic.IndexedDb/key-rotation", state]);
}

function requiredKeys(state, keys) {
    const source = keys.get(state.sourceKeyId);
    const target = keys.get(state.targetKeyId);
    if (!source || !target) throw new Error("MAGIC_ROTATION_KEY_UNAVAILABLE: Both authorized keys are required.");
    return { source, target };
}

export async function sealRotationJournal(state, keys, vault, assertCurrent) {
    const value = canonicalState(state);
    const { source, target } = requiredKeys(state, keys);
    assertCurrent();
    const [sourceSeal, targetSeal] = await Promise.all([
        encryptValue(value, source, state.sourceKeyId, context(vault, "source")),
        encryptValue(value, target, state.targetKeyId, context(vault, "target"))
    ]);
    assertCurrent();
    return { state: structuredClone(state), sourceSeal, targetSeal };
}

export async function verifyRotationJournal(document, keys, vault, assertCurrent) {
    const state = structuredClone(document?.state);
    const value = canonicalState(state);
    const { source, target } = requiredKeys(state, keys);
    assertCurrent();
    let authenticated;
    try {
        authenticated = await Promise.all([
            decryptValue(document.sourceSeal, source, state.sourceKeyId, context(vault, "source")),
            decryptValue(document.targetSeal, target, state.targetKeyId, context(vault, "target"))
        ]);
    } catch {
        assertCurrent();
        throw invalidJournal();
    }
    assertCurrent();
    if (authenticated.some(item => item !== value)) throw invalidJournal();
    return state;
}
