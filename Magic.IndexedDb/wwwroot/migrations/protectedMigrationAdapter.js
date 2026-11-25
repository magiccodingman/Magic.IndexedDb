import { assertEncryptionKey, importEncryptionKey, encryptValue, decryptValue } from "../utilities/encryptionEnvelope.js";
import { openRecord, sealRecord } from "../utilities/encryptionRecord.js";
import { encryptionMetadataStore, encryptionMetadataKey } from "../utilities/encryptionPolicy.js";
import { portableProtectionPolicy, matchesProtectionFingerprint } from "../utilities/protectionPolicy.js";
import { canonicalJson, MigrationError } from "./protocol.js";

export const protectedStateKey = "migration-state";
const adapters = new WeakSet();
export function isProtectedMigrationAdapter(value) { return adapters.has(value); }

export function equalEnvelope(left, right) {
    if (!left || !right || left.version !== right.version || left.keyId !== right.keyId ||
        !(left.nonce instanceof Uint8Array) || !(right.nonce instanceof Uint8Array) ||
        !(left.ciphertext instanceof ArrayBuffer) || !(right.ciphertext instanceof ArrayBuffer)) return false;
    const same = (a, b) => a.byteLength === b.byteLength && a.every((value, index) => value === b[index]);
    return same(left.nonce, right.nonce) && same(new Uint8Array(left.ciphertext), new Uint8Array(right.ciphertext));
}

// Journal authentication must preserve native cursor values. JSON.stringify alone
// loses binary keys and conflates Dates with strings. Every node is tagged so a
// user-controlled property cannot impersonate a native value's encoding.
function authenticatedValue(value) {
    if (value === null) return ["null"];
    if (typeof value === "string" || typeof value === "boolean") return [typeof value, value];
    if (typeof value === "number" && Number.isFinite(value)) return ["number", Object.is(value, -0) ? 0 : value];
    if (value instanceof Date && Number.isFinite(value.getTime())) return ["date", value.getTime()];
    if (value instanceof ArrayBuffer || ArrayBuffer.isView(value)) {
        const bytes = value instanceof ArrayBuffer ? new Uint8Array(value)
            : new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
        return ["binary", Array.from(bytes)];
    }
    if (Array.isArray(value)) return ["array", value.map(authenticatedValue)];
    if (value && typeof value === "object" && Object.getPrototypeOf(value) === Object.prototype) {
        return ["object", Object.keys(value).sort().map(key => [key, authenticatedValue(value[key])])];
    }
    throw new MigrationError("MAGDB_PROTECTION_STATE_INVALID", "The migration state contains an unsupported value.");
}

function error(code, message) { return new MigrationError(code, message); }

/**
 * An ephemeral authorization capability, independent of C# and IndexedDB I/O.
 * Crypto finishes before the engine enters a native write transaction. The
 * engine must call assertCurrent again inside that transaction and compare the
 * exact authenticated policy/state envelopes before committing.
 */
export async function createProtectedMigrationAdapter(options) {
    const { name, account, keyId, key: input, assertCurrent: lease = () => {}, signal } = options ?? {};
    if (![name, account, keyId].every(value => typeof value === "string" && value.length > 0))
        throw error("MAGDB_AUTHORIZATION_REQUIRED", "An account-bound database and authorized key identity are required.");
    if (options.targetKey !== undefined && options.targetKey !== input ||
        options.targetKeyId !== undefined && options.targetKeyId !== keyId)
        throw error("MAGDB_KEY_ROTATION_UNSUPPORTED", "This adapter requires the same data-encryption key throughout migration.");
    const assertCurrent = () => {
        try { signal?.throwIfAborted(); lease(); }
        catch { throw error("MAGDB_AUTHORIZATION_LOST", "Migration authorization is no longer active."); }
    };
    assertCurrent();
    let key;
    const bytes = input instanceof Uint8Array ? new Uint8Array(input) : null;
    try {
        key = bytes === null ? input : await importEncryptionKey(bytes);
        assertEncryptionKey(key);
    } catch {
        throw error("MAGDB_AUTHORIZATION_REJECTED", "A usable AES-GCM key is required.");
    } finally { bytes?.fill(0); }
    assertCurrent();
    const context = (primaryKey, store = encryptionMetadataStore) => ({ database: name, account, store, primaryKey, member: null });
    const policyFor = (schema, store) => {
        const policy = portableProtectionPolicy(schema).policies.get(store);
        if (!policy) throw error("MAGDB_PROTECTION_POLICY_MISMATCH", "The record store is outside the authenticated model.");
        return policy;
    };
    const decrypt = async (envelope, location, code) => {
        assertCurrent();
        let value;
        try { value = await decryptValue(envelope, key, keyId, location); }
        catch { assertCurrent(); throw error(code, "Protected migration data could not be authenticated."); }
        assertCurrent();
        return value;
    };
    const encrypt = async (value, location) => {
        assertCurrent();
        let envelope;
        try { envelope = await encryptValue(value, key, keyId, location); }
        catch { assertCurrent(); throw error("MAGDB_PROTECTION_WRITE_FAILED", "Protected migration data could not be prepared."); }
        assertCurrent();
        return envelope;
    };
    const fenceValue = binding => canonicalJson(["Magic.IndexedDb/migration-fence", 1, binding]);
    const stateValue = state => JSON.stringify(["Magic.IndexedDb/migration-state", 1, authenticatedValue(state)]);
    const adapter = Object.freeze({
        name, account, keyId, signal, assertCurrent,
        fingerprint: schema => portableProtectionPolicy(schema).policyFingerprint,
        async verifyPolicy(envelope, schema, { legacyOnly = false } = {}) {
            const value = await decrypt(envelope, context(encryptionMetadataKey), "MAGDB_AUTHORIZATION_REJECTED");
            let parsed;
            try { parsed = JSON.parse(value); } catch { /* redacted below */ }
            if (!Array.isArray(parsed) || parsed.length !== 3 || parsed[0] !== "Magic.IndexedDb/policy" || parsed[1] !== 1 ||
                !matchesProtectionFingerprint(parsed[2], schema))
                throw error("MAGDB_PROTECTION_POLICY_MISMATCH", "The authenticated protection policy differs from the installed model.");
            // A valid managed sentinel is evidence that authenticated history
            // already existed. Removing its journal/proof must never turn it
            // back into an adoptable legacy vault and replay old transforms.
            const managed = parsed[2] === portableProtectionPolicy(schema).policyFingerprint;
            if (legacyOnly && managed)
                throw error("MAGDB_PROTECTION_STATE_INVALID", "A managed protection policy requires its authenticated migration journal.");
            if (!legacyOnly && !managed)
                throw error("MAGDB_PROTECTION_STATE_INVALID", "An authenticated migration journal requires a managed protection policy.");
            return structuredClone(envelope);
        },
        createPolicy(schema) {
            const fingerprint = portableProtectionPolicy(schema).policyFingerprint;
            return encrypt(JSON.stringify(["Magic.IndexedDb/policy", 1, fingerprint]), context(encryptionMetadataKey));
        },
        createFence: binding => encrypt(fenceValue(binding), context(encryptionMetadataKey)),
        async authenticateFence(envelope, binding) {
            const actual = await decrypt(envelope, context(encryptionMetadataKey), "MAGDB_AUTHORIZATION_REJECTED");
            if (actual !== fenceValue(binding))
                throw error("MAGDB_PROTECTION_STATE_INVALID", "The authenticated migration fence does not match the interrupted edge.");
        },
        sealState: state => encrypt(stateValue(state), context(protectedStateKey)),
        async verifyState(envelope, state) {
            const actual = await decrypt(envelope, context(protectedStateKey), "MAGDB_PROTECTION_STATE_INVALID");
            if (actual !== stateValue(state))
                throw error("MAGDB_PROTECTION_STATE_INVALID", "The migration journal differs from its authenticated state.");
        },
        async openRecord(row, schema, store, primaryKey) {
            assertCurrent();
            let value;
            try { value = await openRecord(row, policyFor(schema, store), key, keyId, context(primaryKey, store)); }
            catch { assertCurrent(); throw error("MAGDB_PROTECTION_RECORD_INVALID", "A source record could not be authenticated under its protection policy."); }
            assertCurrent();
            return value;
        },
        async sealRecord(row, schema, store, primaryKey) {
            assertCurrent();
            let value;
            try { value = await sealRecord(row, policyFor(schema, store), key, keyId, context(primaryKey, store)); }
            catch { assertCurrent(); throw error("MAGDB_PROTECTION_RECORD_INVALID", "A target record could not be encoded under its protection policy."); }
            assertCurrent();
            return value;
        }
    });
    adapters.add(adapter);
    return adapter;
}
