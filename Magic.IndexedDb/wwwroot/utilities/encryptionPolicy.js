import { decryptValue, encryptValue } from "./encryptionEnvelope.js";

export const encryptionMetadataStore = "__magic_encryption";
const metadataKey = "policy";
const policyVersion = 1;

function checkContext(db, account) {
    return {
        database: db.name,
        account,
        store: encryptionMetadataStore,
        primaryKey: metadataKey,
        member: null
    };
}

function checkValue(policyFingerprint) {
    return JSON.stringify(["Magic.IndexedDb/policy", policyVersion, policyFingerprint]);
}

// Creating the sentinel is an explicit operation for empty databases. Ordinary
// unlock never reinitializes missing metadata, even when all stores are empty.
export async function verifyEncryptionPolicy(db, {
    account, policyFingerprint, keyId, key, initialize = false, signal
}) {
    if (typeof policyFingerprint !== "string" || !policyFingerprint) {
        throw new TypeError("Magic encryption requires a storage policy fingerprint.");
    }

    const metadata = db.table(encryptionMetadataStore);
    const context = checkContext(db, account);
    const expected = checkValue(policyFingerprint);
    signal?.throwIfAborted();
    let saved = await metadata.get(metadataKey);

    if (saved === undefined && initialize) {
        const proposed = await encryptValue(expected, key, keyId, context);
        const stores = db.tables.filter(table => table.name !== encryptionMetadataStore);

        saved = await db.transaction("rw", [metadata, ...stores], async () => {
            signal?.throwIfAborted();
            // Read again inside the transaction. Another tab may have won.
            const current = await metadata.get(metadataKey);
            if (current !== undefined) {
                return current;
            }

            for (const table of stores) {
                if (await table.count() !== 0) {
                    throw new Error("Magic encryption initialization requires an empty database.");
                }
            }

            signal?.throwIfAborted();
            await metadata.add(proposed, metadataKey);
            return proposed;
        });
    }

    if (saved === undefined) {
        throw new Error("Magic encryption policy metadata is missing.");
    }

    const actual = await decryptValue(saved, key, keyId, context);
    signal?.throwIfAborted();
    if (actual !== expected) {
        throw new Error("Magic encryption storage policy does not match the persisted policy.");
    }
}
