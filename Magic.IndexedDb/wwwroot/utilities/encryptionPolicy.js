import { decryptValue, encryptValue } from "./encryptionEnvelope.js";
import { parsePolicyDocument, serializePolicyDocument } from "./encryptionPolicyDocument.js";

export const encryptionMetadataStore = "__magic_encryption";
export const encryptionMetadataKey = "policy";

function checkContext(db, account) {
    return {
        database: db.name,
        account,
        store: encryptionMetadataStore,
        primaryKey: encryptionMetadataKey,
        member: null
    };
}

// Creating the sentinel is an explicit operation for empty databases. Ordinary
// unlock never reinitializes missing metadata, even when all stores are empty.
export async function verifyEncryptionPolicy(db, {
    account, policyFingerprint, keyId, key, initialize = false, allowRotated = false, signal
}) {
    if (typeof policyFingerprint !== "string" || !policyFingerprint) {
        throw new TypeError("Magic encryption requires a storage policy fingerprint.");
    }

    if (!db.tables.some(table => table.name === encryptionMetadataStore)) {
        throw new Error("MAGIC_ENCRYPTION_POLICY_MISMATCH: The protected metadata store is missing.");
    }
    const metadata = db.table(encryptionMetadataStore);
    const context = checkContext(db, account);
    const expected = serializePolicyDocument(policyFingerprint);
    signal?.throwIfAborted();
    let saved = await metadata.get(encryptionMetadataKey);

    if (saved === undefined && initialize) {
        const proposed = await encryptValue(expected, key, keyId, context);
        const stores = db.tables.filter(table => table.name !== encryptionMetadataStore);

        saved = await db.transaction("rw", [metadata, ...stores], async () => {
            signal?.throwIfAborted();
            // Read again inside the transaction. Another tab may have won.
            const current = await metadata.get(encryptionMetadataKey);
            if (current !== undefined) {
                return current;
            }

            for (const table of stores) {
                if (await table.count() !== 0) {
                    throw new Error("Magic encryption initialization requires an empty database.");
                }
            }

            signal?.throwIfAborted();
            await metadata.add(proposed, encryptionMetadataKey);
            return proposed;
        });
    }

    if (saved === undefined) {
        throw new Error("MAGIC_ENCRYPTION_UNINITIALIZED: Magic encryption policy metadata is missing.");
    }

    let actual;
    try {
        actual = await decryptValue(saved, key, keyId, context);
    } catch (cause) {
        throw new Error("MAGIC_ENCRYPTION_INVALID_KEY: The key or stored policy envelope could not be authenticated.", { cause });
    }
    signal?.throwIfAborted();
    const policy = parsePolicyDocument(actual, keyId);
    if (!policy || policy.fingerprint !== policyFingerprint || policy.lineage !== null && !allowRotated) {
        throw new Error("MAGIC_ENCRYPTION_POLICY_MISMATCH: Magic encryption storage policy does not match the persisted policy.");
    }
    return saved;
}

function equalBytes(left, right) {
    return left.byteLength === right.byteLength && left.every((value, index) => value === right[index]);
}

// Compare the exact authenticated envelope, not an unauthenticated policy label.
export async function assertPersistedEncryptionPolicy(table, verified) {
    const current = await table.get(encryptionMetadataKey);
    if (!current || current.version !== verified.version || current.keyId !== verified.keyId ||
        !(current.nonce instanceof Uint8Array) || !(current.ciphertext instanceof ArrayBuffer) ||
        !equalBytes(current.nonce, verified.nonce) ||
        !equalBytes(new Uint8Array(current.ciphertext), new Uint8Array(verified.ciphertext))) {
        throw new Error("MAGIC_ENCRYPTION_POLICY_MISMATCH: The authenticated storage policy changed or is missing.");
    }
}
