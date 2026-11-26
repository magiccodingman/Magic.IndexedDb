import { canonicalJson } from "../migrations/protocol.js";
import { assertEncryptionKey, decryptValue, encryptValue } from "./encryptionEnvelope.js";
import { encryptionMetadataKey, encryptionMetadataStore } from "./encryptionPolicy.js";

function context(vault, member) {
    return { ...vault, store: encryptionMetadataStore, primaryKey: encryptionMetadataKey, member };
}

function fenceValue(state) {
    // Batch checkpoints do not invalidate a foreground writer. Admission and
    // entry to the verification barrier do.
    return canonicalJson(["Magic.IndexedDb/rotation-fence", state.version, state.rotationId,
        state.sourceKeyId, state.targetKeyId, state.mode, state.modelHash, state.policyFingerprint,
        state.writeKeyId, state.phase]);
}

export async function assertDistinctRotationKeys(source, target, sourceKeyId, vault, assertCurrent) {
    assertEncryptionKey(source);
    assertEncryptionKey(target);
    assertCurrent();
    const challengeContext = context(vault, "rotation-key-challenge");
    const challenge = await encryptValue(null, source, sourceKeyId, challengeContext);
    let same = true;
    try {
        await decryptValue(challenge, target, sourceKeyId, challengeContext);
    } catch (error) {
        if (error.cause?.name !== "OperationError") throw error;
        same = false;
    }
    assertCurrent();
    if (same) throw new Error("MAGIC_ROTATION_KEY_REUSED: The target must be a different data encryption key.");
}

export async function sealRotationFence(state, target, vault, assertCurrent) {
    assertCurrent();
    // Keep the policy location and ordinary AAD layout. Legacy clients cannot
    // interpret the authenticated rotation value as a normal protection policy.
    const seal = await encryptValue(fenceValue(state), target, state.targetKeyId, context(vault, null));
    assertCurrent();
    return seal;
}

export async function verifyRotationFence(seal, state, target, vault, assertCurrent) {
    assertCurrent();
    let actual;
    try {
        actual = await decryptValue(seal, target, state.targetKeyId, context(vault, null));
    } catch {
        assertCurrent();
        throw new Error("MAGIC_ROTATION_STATE_INVALID: The rotation fence could not be authenticated.");
    }
    assertCurrent();
    if (actual !== fenceValue(state)) {
        throw new Error("MAGIC_ROTATION_STATE_INVALID: The rotation fence differs from the admitted state.");
    }
}
