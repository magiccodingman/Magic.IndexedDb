import { decryptValue, encryptValue } from "./encryptionEnvelope.js";

export const encryptedRecordMember = "__magic_record";

function assertRecord(value) {
    if (value === null || typeof value !== "object" || Array.isArray(value)) {
        throw new TypeError("Magic encryption requires a serialized record object.");
    }
}

function selectMembers(record, keep) {
    return Object.fromEntries(Object.entries(record).filter(([name]) => keep(name)));
}

// Policies describe persisted member names. A null exposed set selects field
// encryption. Otherwise the exposed set is the complete plaintext allowlist.
export async function sealRecord(record, policy, key, keyId, context) {
    assertRecord(record);
    if (Object.hasOwn(record, encryptedRecordMember)) {
        throw new Error("The record uses a reserved Magic encryption member.");
    }

    if (policy.exposed !== null) {
        const publicMembers = selectMembers(record, name => policy.exposed.has(name));
        const payload = selectMembers(record, name => !policy.exposed.has(name));
        publicMembers[encryptedRecordMember] = await encryptValue(payload, key, keyId, { ...context, member: null });
        return publicMembers;
    }

    const publicMembers = selectMembers(record, name => !policy.fields.has(name));
    const protectedMembers = await Promise.all(Array.from(policy.fields, async member => {
        const value = Object.hasOwn(record, member) ? [record[member]] : [];
        return [member, await encryptValue(value, key, keyId, { ...context, member })];
    }));
    return { ...publicMembers, ...Object.fromEntries(protectedMembers) };
}

export async function openRecord(record, policy, key, keyId, context) {
    assertRecord(record);
    if (policy.exposed !== null) {
        const publicMembers = selectMembers(record, name => policy.exposed.has(name));
        const payload = await decryptValue(record[encryptedRecordMember], key, keyId, { ...context, member: null });
        assertRecord(payload);
        return { ...publicMembers, ...selectMembers(payload, name => !policy.exposed.has(name) && name !== encryptedRecordMember) };
    }

    const publicMembers = selectMembers(record, name => !policy.fields.has(name) && name !== encryptedRecordMember);
    const protectedMembers = await Promise.all(Array.from(policy.fields, async member => {
        const value = await decryptValue(record[member], key, keyId, { ...context, member });
        if (!Array.isArray(value) || value.length > 1) {
            throw new Error("Invalid Magic encrypted field value.");
        }
        return value.length === 0 ? [] : [[member, value[0]]];
    }));
    return { ...publicMembers, ...Object.fromEntries(protectedMembers.flat()) };
}
