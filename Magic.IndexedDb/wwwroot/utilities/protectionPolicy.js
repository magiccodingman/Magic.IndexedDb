// The protected CRUD and migration paths share one policy interpretation. The
// legacy fingerprint remains byte-for-byte stable; managed fingerprints describe
// the portable native layout and protection contract explicitly.
const metadataStore = "__magic_encryption";
const recordMember = "__magic_record";

function fail() { throw new Error("MAGDB_PROTECTION_POLICY_INVALID: Unsupported protected storage policy."); }
function member(name) {
    if (typeof name !== "string" || !name || name.includes(".") ||
        [recordMember, "__proto__", "constructor", "prototype"].includes(name)) fail();
    return name;
}
function members(names) {
    if (!Array.isArray(names)) fail();
    return new Set(names.map(member).sort());
}
function compare(left, right) { return left < right ? -1 : left > right ? 1 : 0; }
function format(numericStorage) {
    if (numericStorage === null) return "legacy";
    if (numericStorage?.version !== 1 || numericStorage.mode !== "lossless" ||
        Object.keys(numericStorage).length !== 2) fail();
    return "lossless-v1";
}

function storePolicy(store) {
    if (typeof store.name !== "string" || !store.name || store.name === metadataStore ||
        store.name.startsWith("__magic_") || typeof store.autoIncrement !== "boolean") fail();
    const keyPaths = store.keyPath === null ? [] : typeof store.keyPath === "string" ? [store.keyPath] : store.keyPath;
    if (!Array.isArray(keyPaths) || store.keyPath !== null && !keyPaths.length) fail();
    keyPaths.forEach(member);
    const fields = members(store.encryptedFields ?? []);
    const exposed = store.exposedFields == null ? null : members(store.exposedFields);
    const protectsRows = fields.size !== 0 || exposed !== null;
    if (protectsRows && (store.autoIncrement || !keyPaths.length)) fail();
    if (!Array.isArray(store.indexes)) fail();
    const names = new Set();
    for (const index of store.indexes) {
        if (typeof index.name !== "string" || !index.name || names.has(index.name) ||
            typeof index.unique !== "boolean" || typeof index.multiEntry !== "boolean") fail();
        names.add(index.name);
        const parts = typeof index.keyPath === "string" ? [index.keyPath] : index.keyPath;
        if (!Array.isArray(parts) || !parts.length || index.multiEntry && Array.isArray(index.keyPath)) fail();
        parts.forEach(member);
    }
    for (const name of [...keyPaths, ...store.indexes.flatMap(index => Array.isArray(index.keyPath) ? index.keyPath : [index.keyPath])]) {
        if (fields.has(name) || exposed !== null && !exposed.has(name)) fail();
    }
    if (exposed !== null && [...fields].some(name => exposed.has(name))) fail();
    return { fields, exposed, keyPaths: [...keyPaths] };
}

export function portableProtectionPolicy(schema) {
    if (!schema || !["legacy", "lossless-v1"].includes(schema.numericFormat) || !Array.isArray(schema.stores)) fail();
    const policies = new Map();
    const stores = [...schema.stores].sort((a, b) => compare(a.name, b.name)).map(store => {
        if (policies.has(store.name)) fail();
        const policy = storePolicy(store);
        policies.set(store.name, policy);
        return {
            name: store.name, keyPath: Array.isArray(store.keyPath) ? [...store.keyPath] : store.keyPath,
            autoIncrement: store.autoIncrement,
            indexes: [...store.indexes].sort((a, b) => compare(a.name, b.name)).map(index => ({
                name: index.name, keyPath: Array.isArray(index.keyPath) ? [...index.keyPath] : index.keyPath,
                unique: index.unique, multiEntry: index.multiEntry
            })),
            encryptedFields: [...policy.fields], exposedFields: policy.exposed === null ? null : [...policy.exposed]
        };
    });
    const contract = { numericFormat: schema.numericFormat, stores };
    return { policies, contract, policyFingerprint: JSON.stringify(["Magic.IndexedDb/managed-policy", 1, contract]) };
}

function legacyStore(schema) {
    const indexes = new Map();
    for (const name of schema.indexes) if (!schema.uniqueIndexes.includes(name))
        indexes.set(name, { name, keyPath: name, unique: false, multiEntry: false });
    for (const name of schema.uniqueIndexes) indexes.set(name, { name, keyPath: name, unique: true, multiEntry: false });
    for (const parts of schema.columnNamesInCompoundIndex) {
        if (!Array.isArray(parts) || !parts.length) fail();
        const name = `[${parts.join("+")}]`;
        if (indexes.has(name)) fail();
        indexes.set(name, { name, keyPath: [...parts], unique: false, multiEntry: false });
    }
    const keys = schema.columnNamesInCompoundKey;
    return { name: schema.tableName, keyPath: keys.length === 0 ? null : keys.length === 1 ? keys[0] : keys,
        autoIncrement: schema.primaryKeyAuto, indexes: [...indexes.values()],
        encryptedFields: schema.encryptedFields, exposedFields: schema.exposedFields };
}

export function createProtectionPolicy(schemas, numericStorage = null, options = {}) {
    if (!Array.isArray(schemas)) fail();
    const fingerprint = [];
    const native = [];
    const names = new Set();
    let hasProtection = false;
    for (const schema of [...schemas].sort((a, b) => compare(a.tableName, b.tableName))) {
        if (!schema.tableName || names.has(schema.tableName) || !Array.isArray(schema.indexes) ||
            !Array.isArray(schema.uniqueIndexes) || !Array.isArray(schema.columnNamesInCompoundIndex) ||
            !Array.isArray(schema.columnNamesInCompoundKey)) fail();
        names.add(schema.tableName);
        const fields = members(schema.encryptedFields ?? []);
        const exposed = schema.exposedFields == null ? null : members(schema.exposedFields);
        hasProtection ||= fields.size !== 0 || exposed !== null;
        fingerprint.push([schema.tableName, schema.columnNamesInCompoundKey, Boolean(schema.primaryKeyAuto),
            [...schema.indexes].sort(), [...schema.uniqueIndexes].sort(),
            schema.columnNamesInCompoundIndex.map(index => JSON.stringify(index)).sort(),
            [...fields], exposed === null ? null : [...exposed]]);
        native.push(legacyStore({ ...schema, primaryKeyAuto: Boolean(schema.primaryKeyAuto) }));
    }
    if (!hasProtection && !options.allowEmptyProtection) fail();
    const numericFormat = format(numericStorage);
    const normalized = portableProtectionPolicy({ numericFormat, stores: native });
    if (options.managedSchema) {
        const managed = portableProtectionPolicy(options.managedSchema);
        if (JSON.stringify(normalized.contract) !== JSON.stringify(managed.contract)) fail();
        return managed;
    }
    return { ...normalized, policyFingerprint: numericStorage === null ? JSON.stringify(fingerprint)
        : JSON.stringify({ schema: fingerprint, numericStorage }) };
}

// A legacy sentinel records the original declaration syntax, which can include
// both [Index] and [UniqueIndex] for one native index. Authenticate its bytes
// first, then compare the complete normalized contract; never guess its policy.
export function matchesProtectionFingerprint(fingerprint, schema) {
    if (typeof fingerprint !== "string") return false;
    const expected = portableProtectionPolicy(schema);
    if (fingerprint === expected.policyFingerprint) return true;
    try {
        let parsed = JSON.parse(fingerprint), numericStorage = null;
        if (!Array.isArray(parsed)) {
            if (!parsed || Object.keys(parsed).length !== 2 || !Object.hasOwn(parsed, "schema") ||
                !Object.hasOwn(parsed, "numericStorage")) return false;
            numericStorage = parsed.numericStorage;
            parsed = parsed.schema;
        }
        if (!Array.isArray(parsed)) return false;
        const schemas = parsed.map(entry => {
            if (!Array.isArray(entry) || entry.length !== 8 || typeof entry[2] !== "boolean" ||
                !Array.isArray(entry[1]) || !Array.isArray(entry[3]) || !Array.isArray(entry[4]) ||
                !Array.isArray(entry[5]) || !Array.isArray(entry[6]) || entry[7] !== null && !Array.isArray(entry[7])) fail();
            return { tableName: entry[0], columnNamesInCompoundKey: entry[1], primaryKeyAuto: entry[2],
                indexes: entry[3], uniqueIndexes: entry[4], columnNamesInCompoundIndex: entry[5].map(value => {
                    if (typeof value !== "string") fail();
                    return JSON.parse(value);
                }), encryptedFields: entry[6], exposedFields: entry[7] };
        });
        const actual = createProtectionPolicy(schemas, numericStorage, { allowEmptyProtection: true });
        return JSON.stringify(actual.contract) === JSON.stringify(expected.contract);
    } catch { return false; }
}
