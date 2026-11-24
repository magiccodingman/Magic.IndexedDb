import { importEncryptionKey } from "./encryptionEnvelope.js";
import { assertPersistedEncryptionPolicy, verifyEncryptionPolicy, encryptionMetadataKey, encryptionMetadataStore } from "./encryptionPolicy.js";
import { openRecord, sealRecord, encryptedRecordMember } from "./encryptionRecord.js";
import { EncryptionSession } from "./encryptionSession.js";
import { magicQueryYield } from "../magicLinqToIndexedDb.js";

export const protectedDatabasePrefix = "__magic_protected__:";
const locked = "MAGIC_ENCRYPTION_LOCKED";
const encryptionBatchSize = 32;

function validateMember(name) {
    if (typeof name !== "string" || !name || name.includes(".") ||
        [encryptedRecordMember, "__proto__", "constructor", "prototype"].includes(name)) {
        throw new Error("Invalid or reserved Magic encryption member.");
    }
}

function normalizeMembers(names) {
    for (const name of names) validateMember(name);
    return new Set([...names].sort());
}

// Native open runs schema creation only for a genuinely new database. Opening
// existing databases with Dexie declarations can repair/add indexes before a
// caller has authenticated the persisted policy.
export function ensureProtectedSchema(name, schemas) {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(name);
        request.onupgradeneeded = event => {
            if (event.oldVersion !== 0) {
                request.transaction.abort();
                return;
            }
            try {
                const db = request.result;
                db.createObjectStore(encryptionMetadataStore);
                for (const schema of schemas) {
                    const keys = schema.columnNamesInCompoundKey;
                    const keyPath = keys.length === 1 ? keys[0] : keys;
                    const store = db.createObjectStore(schema.tableName, {
                        keyPath, autoIncrement: Boolean(schema.primaryKeyAuto)
                    });
                    for (const member of new Set(schema.indexes)) {
                        if (!schema.uniqueIndexes.includes(member)) store.createIndex(member, member);
                    }
                    for (const member of new Set(schema.uniqueIndexes)) {
                        store.createIndex(member, member, { unique: true });
                    }
                    for (const parts of schema.columnNamesInCompoundIndex) {
                        const index = `[${parts.join("+")}]`;
                        if (parts.length && !store.indexNames.contains(index)) store.createIndex(index, parts);
                    }
                }
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

export function createProtectedStorage(db, account, schemas) {
    const policies = new Map();
    const schemaFingerprint = [];
    let hasProtection = false;
    for (const schema of [...schemas].sort((left, right) =>
        left.tableName < right.tableName ? -1 : Number(left.tableName !== right.tableName))) {
        if (!schema.tableName || schema.tableName === encryptionMetadataStore ||
            schemaFingerprint.some(entry => entry[0] === schema.tableName)) {
            throw new Error("Invalid or duplicate store in Magic encryption policy.");
        }
        const keyPaths = schema.columnNamesInCompoundKey;
        const indexes = [...schema.indexes, ...schema.uniqueIndexes, ...schema.columnNamesInCompoundIndex.flat()];
        const fields = normalizeMembers(schema.encryptedFields ?? []);
        const exposed = schema.exposedFields == null ? null : normalizeMembers(schema.exposedFields);
        schemaFingerprint.push([schema.tableName, keyPaths, Boolean(schema.primaryKeyAuto),
            [...schema.indexes].sort(), [...schema.uniqueIndexes].sort(), schema.columnNamesInCompoundIndex.map(index => JSON.stringify(index)).sort(),
            [...fields], exposed && [...exposed]]);
        const protectsRows = fields.size !== 0 || exposed !== null;
        hasProtection ||= protectsRows;
        if (protectsRows && (schema.primaryKeyAuto || !keyPaths.length)) {
            throw new Error("Protected Magic stores require explicit primary keys.");
        }
        for (const name of [...keyPaths, ...indexes]) {
            validateMember(name);
            if (fields.has(name) || (exposed && !exposed.has(name))) {
                throw new Error("Protected members cannot be primary keys or indexes.");
            }
        }
        if (exposed && [...fields].some(name => exposed.has(name))) {
            throw new Error("An encrypted member cannot be exposed by the record policy.");
        }
        policies.set(schema.tableName, { fields, exposed, keyPaths });
    }

    if (!hasProtection) throw new Error("Magic encryption configuration contains no protected stores.");
    const session = new EncryptionSession();
    const policyFingerprint = JSON.stringify(schemaFingerprint);
    let verified;
    let mismatch = false;
    const assertPolicy = async () => {
        if (!verified) {
            throw new Error("MAGIC_ENCRYPTION_POLICY_REQUIRED: Unlock this connection to authenticate its storage policy.");
        }
        await assertPersistedEncryptionPolicy(db.table(encryptionMetadataStore), verified);
    };

    return {
        forTable: name => {
            const policy = policies.get(name);
            if (!policy) throw new Error("MAGIC_ENCRYPTION_POLICY_REQUIRED: The store is outside the configured storage policy.");
            return new ProtectedTable(db, account, name, policy, session, assertPolicy);
        },
        unlock: (keyId, input, initialize) => session.ensureUnlocked(async signal => {
            await db.open();
            signal.throwIfAborted();
            const key = input instanceof Uint8Array ? await importEncryptionKey(input) : input;
            try {
                if (verified) await assertPolicy();
                verified = await verifyEncryptionPolicy(db, { account, policyFingerprint, keyId, key, initialize, signal });
                mismatch = false;
                return { key, keyId };
            } catch (error) {
                mismatch ||= error.message.startsWith("MAGIC_ENCRYPTION_POLICY_MISMATCH:");
                throw error;
            }
        }),
        isUnlocked: async () => {
            if (!session.isUnlocked) return false;
            await assertPolicy();
            return session.isUnlocked;
        },
        state: async () => {
            await db.open();
            if (!verified) {
                if (mismatch || !db.tables.some(table => table.name === encryptionMetadataStore)) return "PolicyMismatch";
                return await db.table(encryptionMetadataStore).get(encryptionMetadataKey) === undefined
                    ? "Uninitialized" : "Locked";
            }
            try {
                await assertPolicy();
                return session.isUnlocked ? "Unlocked" : "Locked";
            } catch (error) {
                if (!error.message.startsWith("MAGIC_ENCRYPTION_POLICY_MISMATCH:")) throw error;
                session.lock();
                return "PolicyMismatch";
            }
        },
        lock: () => session.lock(),
        dispose: () => {
            verified = undefined;
            session.dispose();
        }
    };
}

class ProtectedTable {
    constructor(db, account, name, policy, session, assertPolicy) {
        this.db = db;
        this.table = db.table(name);
        this.account = account;
        this.policy = policy;
        this.session = session;
        this.assertPolicy = assertPolicy;
    }

    context(primaryKey) {
        return { database: this.db.name, account: this.account, store: this.table.name, primaryKey };
    }

    key(record) {
        const parts = this.policy.keyPaths.map(name => record[name]);
        return parts.length === 1 ? parts[0] : parts;
    }

    isPublic(name) {
        if (typeof name !== "string" || name === encryptedRecordMember) return false;
        const root = name.split(".")[0];
        return this.policy.exposed !== null ? this.policy.exposed.has(root) : !this.policy.fields.has(root);
    }

    requirePublic(names) {
        if (names.some(name => !this.isPublic(name))) {
            throw new Error("MAGIC_ENCRYPTION_QUERY_UNSUPPORTED: The operation references a protected member.");
        }
    }

    validateQuery(node, additions) {
        if (node?.condition?.property && node.condition.property !== "__constant") {
            this.requirePublic([node.condition.property]);
        }
        for (const child of node?.children ?? []) this.validateQuery(child, []);
        this.requirePublic((additions ?? []).filter(addition => addition.property).map(addition => addition.property));
    }

    assertLease(lease) {
        try {
            lease();
        } catch {
            throw new Error(`${locked}: The protected query was locked during execution.`);
        }
    }

    async access(operation) {
        try {
            return await this.session.useKey(operation);
        } catch (error) {
            if (error.message.startsWith("Magic encryption session is locked") ||
                error.message.startsWith("Magic encryption session was locked")) {
                throw new Error(`${locked}: Unlock the database before accessing a protected record.`);
            }
            throw error;
        }
    }

    async fencedWrite(operation, assertCurrent = () => {}) {
        await this.db.open();
        return this.db.transaction("rw", [this.table, this.db.table(encryptionMetadataStore)], async () => {
            await this.assertPolicy();
            assertCurrent();
            return operation(this.table);
        });
    }

    mutate(operation) {
        return this.access((_, assertCurrent) => this.fencedWrite(operation, assertCurrent));
    }

    write(records, operation) {
        return this.access(async ({ key, keyId }, assertCurrent) => {
            const sealed = [];
            for (let offset = 0; offset < records.length; offset += encryptionBatchSize) {
                assertCurrent();
                const batch = records.slice(offset, offset + encryptionBatchSize);
                sealed.push(...await Promise.all(batch.map(record =>
                    sealRecord(record, this.policy, key, keyId, this.context(this.key(record))))));
            }
            return this.fencedWrite(table => operation(table, sealed), assertCurrent);
        });
    }

    async patch(primaryKey, changes) {
        const names = Object.keys(changes);
        for (const name of names) validateMember(name);
        this.requirePublic(names);
        if (names.some(name => this.policy.keyPaths.includes(name))) {
            throw new Error("A Magic public patch cannot change the primary key.");
        }
        return this.fencedWrite(table => table.update(primaryKey, changes));
    }

    async *query(predicate, additions, forceCursor, projection = null) {
        if (projection !== null) this.requirePublic(projection);
        // Even an empty full query requires an unlocked session.
        const lease = projection === null ? await this.access((_, assertCurrent) => assertCurrent) : null;
        this.validateQuery(predicate, additions);
        await this.db.open();
        await this.assertPolicy();
        for await (const row of magicQueryYield(this.db, this.table, predicate, additions, forceCursor)) {
            if (projection !== null) {
                yield Object.fromEntries(projection.filter(name => Object.hasOwn(row, name)).map(name => [name, row[name]]));
                continue;
            }
            this.assertLease(lease);
            const opened = await this.access(({ key, keyId }) =>
                openRecord(row, this.policy, key, keyId, this.context(this.key(row))));
            this.assertLease(lease);
            yield opened;
        }
        if (lease) this.assertLease(lease);
        else await this.assertPolicy();
    }

    async count() {
        await this.db.open();
        await this.assertPolicy();
        return this.table.count();
    }

    read(primaryKey) {
        return this.access(async ({ key, keyId }) => {
            await this.assertPolicy();
            const row = await this.table.get(primaryKey);
            return row === undefined ? undefined : openRecord(row, this.policy, key, keyId, this.context(primaryKey));
        });
    }
}
