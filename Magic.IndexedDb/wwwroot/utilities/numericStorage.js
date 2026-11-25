export const numericDatabasePrefix = "__magic_numeric_v1__:";
export const numericMetadataStore = "__magic_numeric_policy";
const numericMetadataKey = "policy";

// Lossless encoding changes both stored values and native index keys. Keep its
// databases in a separate namespace so older clients continue to use Legacy
// storage. This policy describes an immutable native schema; changing it needs
// an explicit export/recreate or discard/refetch decision by the application.
export function normalizeNumericStorage(value) {
    if (value === undefined) return null;
    if (value === null || typeof value !== "object" || value.version !== 1 || value.mode !== "lossless" ||
        Object.keys(value).some(name => name !== "version" && name !== "mode")) {
        throw new Error("MAGIC_NUMERIC_POLICY_UNSUPPORTED: The numeric storage policy is not supported.");
    }
    return { version: 1, mode: "lossless" };
}

function sortedIndexes(indexes) {
    return indexes.sort((left, right) => left.name < right.name ? -1 : Number(left.name !== right.name));
}

function schemaPolicy(schemas) {
    if (!Array.isArray(schemas)) throw new Error("MAGIC_NUMERIC_POLICY_INVALID: A store schema is required.");
    const names = new Set();
    return schemas.map(schema => {
        if (typeof schema.tableName !== "string" || !schema.tableName ||
            schema.tableName === numericMetadataStore || names.has(schema.tableName)) {
            throw new Error("MAGIC_NUMERIC_POLICY_INVALID: Invalid, duplicate or reserved numeric store name.");
        }
        names.add(schema.tableName);
        const keys = schema.columnNamesInCompoundKey;
        if (!Array.isArray(keys) || !keys.length || keys.some(key => typeof key !== "string" || !key)) {
            throw new Error("MAGIC_NUMERIC_POLICY_INVALID: Numeric stores require an inline primary key.");
        }
        const indexes = new Map();
        const addIndex = (name, keyPath, unique) => {
            const index = { name, keyPath, unique, multiEntry: false };
            const existing = indexes.get(name);
            if (existing && JSON.stringify(existing) !== JSON.stringify(index)) {
                throw new Error("MAGIC_NUMERIC_POLICY_INVALID: Conflicting native index definitions.");
            }
            indexes.set(name, index);
        };
        for (const name of schema.indexes ?? []) {
            if (!(schema.uniqueIndexes ?? []).includes(name)) addIndex(name, name, false);
        }
        for (const name of schema.uniqueIndexes ?? []) addIndex(name, name, true);
        for (const parts of schema.columnNamesInCompoundIndex ?? []) {
            if (parts.length) addIndex(`[${parts.join("+")}]`, [...parts], false);
        }
        return {
            name: schema.tableName,
            keyPath: keys.length === 1 ? keys[0] : [...keys],
            autoIncrement: Boolean(schema.primaryKeyAuto),
            indexes: sortedIndexes([...indexes.values()])
        };
    }).sort((left, right) => left.name < right.name ? -1 : Number(left.name !== right.name));
}

export function createNumericPolicy(schemas, policy) {
    return { ...policy, schema: schemaPolicy(schemas) };
}

export function numericPolicyIdentity(policy) {
    return JSON.stringify(policy);
}

export function numericDatabaseExists(name) {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(name);
        let absent = false;
        request.onupgradeneeded = () => {
            absent = true;
            request.transaction.abort();
        };
        request.onerror = () => absent ? resolve(false) : reject(request.error);
        request.onsuccess = () => {
            request.result.close();
            resolve(true);
        };
    });
}

export async function requireFreshNumericTarget(name, legacyName) {
    if (typeof legacyName !== "string" || !legacyName || legacyName === name) {
        throw new Error("MAGIC_NUMERIC_POLICY_INVALID: Lossless storage requires its distinct legacy database name.");
    }
    if (!await numericDatabaseExists(name) && await numericDatabaseExists(legacyName)) {
        throw new Error("MAGIC_NUMERIC_MIGRATION_REQUIRED: A legacy database already exists. Export to an explicitly named new database, or explicitly discard and refetch the legacy data before enabling lossless storage.");
    }
}

export function ensureNumericSchema(name, policy) {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(name);
        request.onupgradeneeded = event => {
            if (event.oldVersion !== 0) {
                request.transaction.abort();
                return;
            }
            try {
                const db = request.result;
                const metadata = db.createObjectStore(numericMetadataStore);
                for (const schema of policy.schema) {
                    const store = db.createObjectStore(schema.name, {
                        keyPath: schema.keyPath, autoIncrement: schema.autoIncrement
                    });
                    for (const index of schema.indexes) {
                        store.createIndex(index.name, index.keyPath, {
                            unique: index.unique, multiEntry: index.multiEntry
                        });
                    }
                }
                metadata.add(policy, numericMetadataKey);
            } catch (error) {
                request.transaction.abort();
                reject(error);
            }
        };
        request.onerror = () => reject(request.error);
        request.onsuccess = () => {
            request.result.close();
            resolve();
        };
    });
}

export async function assertNumericPolicy(db, expected) {
    const metadata = db.tables.find(table => table.name === numericMetadataStore);
    if (!metadata || metadata.schema.primKey.keyPath || metadata.schema.primKey.auto || metadata.schema.indexes.length) {
        throw new Error("MAGIC_NUMERIC_POLICY_MISMATCH: The persisted numeric policy store is missing or changed.");
    }
    const saved = await metadata.get(numericMetadataKey);
    if (!saved || numericPolicyIdentity(saved) !== numericPolicyIdentity(expected)) {
        throw new Error("MAGIC_NUMERIC_POLICY_MISMATCH: The persisted numeric storage policy does not match this configuration.");
    }
    const actualSchema = db.tables.filter(table => table.name !== numericMetadataStore).map(table => ({
        name: table.name,
        keyPath: table.schema.primKey.keyPath,
        autoIncrement: Boolean(table.schema.primKey.auto),
        indexes: sortedIndexes(table.schema.indexes.map(index => ({
            name: index.name, keyPath: index.keyPath, unique: Boolean(index.unique), multiEntry: Boolean(index.multi)
        })))
    })).sort((left, right) => left.name < right.name ? -1 : Number(left.name !== right.name));
    if (JSON.stringify(actualSchema) !== JSON.stringify(expected.schema)) {
        throw new Error("MAGIC_NUMERIC_POLICY_MISMATCH: The native database schema does not match its numeric storage policy.");
    }
}
