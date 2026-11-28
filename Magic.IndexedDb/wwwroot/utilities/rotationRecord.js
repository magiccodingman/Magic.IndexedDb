import { encryptedRecordMember, openRecord, sealRecord } from "./encryptionRecord.js";

function recordGeneration(record, policy) {
    const members = policy.exposed !== null ? [encryptedRecordMember] : [...policy.fields];
    if (!members.length) return null;

    const keyId = record[members[0]]?.keyId;
    if (typeof keyId !== "string" || !keyId || members.some(member => record[member]?.keyId !== keyId)) {
        throw new Error("MAGIC_ROTATION_RECORD_INVALID: The protected record has inconsistent key identifiers.");
    }
    return keyId;
}

// A stored key identifier only selects a candidate key. Every protected member
// must still authenticate to that key and its original IndexedDB location.
export async function openRotatingRecord(record, policy, authorizedKeys, context) {
    const keyId = recordGeneration(record, policy);
    if (keyId === null) return { record, keyId };

    const key = authorizedKeys.get(keyId);
    if (!key) throw new Error("MAGIC_ROTATION_KEY_UNAVAILABLE: An authorized record key is required.");

    try {
        return { record: await openRecord(record, policy, key, keyId, context), keyId };
    } catch {
        throw new Error("MAGIC_ROTATION_RECORD_INVALID: The protected record could not be authenticated.");
    }
}

export async function prepareRotatedRecord(record, policy, authorizedKeys, targetKeyId, context) {
    const targetKey = authorizedKeys.get(targetKeyId);
    if (!targetKey) throw new Error("MAGIC_ROTATION_KEY_UNAVAILABLE: The authorized target key is required.");

    const opened = await openRotatingRecord(record, policy, authorizedKeys, context);
    if (opened.keyId === null || opened.keyId === targetKeyId) return null;

    return sealRecord(opened.record, policy, targetKey, targetKeyId, context);
}
