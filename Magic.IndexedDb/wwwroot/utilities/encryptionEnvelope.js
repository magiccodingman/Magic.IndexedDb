const envelopeVersion = 1;
const nonceBytes = 12;
const tagBits = 128;
const encoder = new TextEncoder();
const decoder = new TextDecoder("utf-8", { fatal: true });

export async function importEncryptionKey(bytes) {
    if (!(bytes instanceof Uint8Array) || bytes.byteLength !== 32) {
        throw new TypeError("Magic encryption requires exactly 32 key bytes.");
    }

    return crypto.subtle.importKey("raw", bytes, "AES-GCM", false, ["encrypt", "decrypt"]);
}

function assertKey(key) {
    if (!(key instanceof CryptoKey) || key.type !== "secret" ||
        key.algorithm.name !== "AES-GCM" || key.algorithm.length !== 256 ||
        !key.usages.includes("encrypt") || !key.usages.includes("decrypt")) {
        throw new TypeError("Magic encryption requires an AES-GCM 256-bit key usable for encryption and decryption.");
    }
}

function isKeyPart(value) {
    return typeof value === "string" || Number.isFinite(value);
}

function associatedData(context, keyId) {
    const { database, account, store, primaryKey, member } = context;
    if (![database, account, store, keyId].every(value => typeof value === "string" && value.length > 0) ||
        (member !== null && (typeof member !== "string" || !member)) ||
        !(Array.isArray(primaryKey) ? primaryKey.length > 0 && [...primaryKey].every(isKeyPart) : isKeyPart(primaryKey))) {
        throw new TypeError("Magic encryption requires a complete storage context and a supported primary key.");
    }

    return encoder.encode(JSON.stringify([
        "Magic.IndexedDb/envelope", envelopeVersion, globalThis.location.origin,
        database, account, store, primaryKey, member, keyId
    ]));
}

export async function encryptValue(value, key, keyId, context) {
    assertKey(key);
    const additionalData = associatedData(context, keyId);
    const json = JSON.stringify(value);
    if (json === undefined) {
        throw new TypeError("Magic encryption requires a JSON value.");
    }

    const nonce = crypto.getRandomValues(new Uint8Array(nonceBytes));
    const plaintext = encoder.encode(json);
    try {
        const ciphertext = await crypto.subtle.encrypt(
            { name: "AES-GCM", iv: nonce, additionalData, tagLength: tagBits }, key, plaintext);
        return { version: envelopeVersion, keyId, nonce, ciphertext };
    } finally {
        plaintext.fill(0);
    }
}

export async function decryptValue(envelope, key, expectedKeyId, context) {
    assertKey(key);
    if (envelope?.version !== envelopeVersion || envelope.keyId !== expectedKeyId ||
        !(envelope.nonce instanceof Uint8Array) || envelope.nonce.byteLength !== nonceBytes ||
        !(envelope.ciphertext instanceof ArrayBuffer) || envelope.ciphertext.byteLength < tagBits / 8) {
        throw new TypeError("Invalid or unsupported Magic encryption envelope.");
    }

    const additionalData = associatedData(context, expectedKeyId);
    let plaintext;
    try {
        plaintext = new Uint8Array(await crypto.subtle.decrypt(
            { name: "AES-GCM", iv: envelope.nonce, additionalData, tagLength: tagBits },
            key, envelope.ciphertext));
    } catch (cause) {
        if (cause.name !== "OperationError") {
            throw cause;
        }
        throw new Error("Magic encryption envelope authentication failed.", { cause });
    }

    try {
        return JSON.parse(decoder.decode(plaintext));
    } finally {
        plaintext.fill(0);
    }
}
